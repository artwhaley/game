using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// The low-level reusable Phase interpreter (Ticket 07): steps a Phase graph
    /// until it yields at a card boundary, transfers flow, completes, or errors.
    ///
    /// Structural validation at construction: exactly one PhaseEntry, node/port/edge
    /// dictionaries built once. Execution: Entry→normal; CardExecutor→one-card
    /// budget semantics (phase tag filters + the run's own Card RNG); VariableCheck→
    /// True/False from live state; ActionNode→sequence then normal; PhaseDecision→
    /// prompt + selected option sequence then common normal; ReturnNode→Return
    /// semantics placeholder (wired by Ticket 08). A deterministic node-step ceiling
    /// per Advance catches non-yield cycles with a loud, identifiable error.
    /// </summary>
    public sealed class PhaseGraphVm
    {
        /// <summary>Deterministic ceiling per Advance; non-yield cycles trip it.</summary>
        public const int MaxStepsPerAdvance = 100000;

        private readonly ContentCatalog _catalog;
        private readonly CoreServices _services;
        private readonly BackgroundActionTracker _tracker;
        private readonly ActionExecutor _executor;
        private readonly CardSelector _cardSelector;
        private readonly PhaseDefinition _phase;

        private readonly Dictionary<string, GraphNodeDefinition> _nodesById;
        private readonly Dictionary<string, GraphOutputDefinition> _outputsById;
        private readonly Dictionary<string, string> _edgeFromOutput; // outputId -> targetNodeId

        public event Action<string> PhaseEntered;      // phase id
        public event Action<string> PhaseNodeChanged;  // node id
        public event Action<CardDefinition> CardStarted;
        public event Action<CardDefinition> CardFinished;
        public event Action<VariableCheckNodeDefinition, float, bool> VariableCheckEvaluated;
        public event Action<string> RuntimeError;      // message

        public PhaseGraphVm(GameContentDefinition content, PhaseDefinition phase, CoreServices services, BackgroundActionTracker tracker)
        {
            _catalog = new ContentCatalog(content);
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _executor = new ActionExecutor(_tracker);
            _cardSelector = new CardSelector(content.Deck, _catalog);
            _phase = phase ?? throw new ArgumentNullException(nameof(phase));

            if (phase.Graph == null) throw new InvalidOperationException($"Phase '{phase.Id}' has no graph.");

            _nodesById = new Dictionary<string, GraphNodeDefinition>();
            _outputsById = new Dictionary<string, GraphOutputDefinition>();
            _edgeFromOutput = new Dictionary<string, string>();

            var entryCount = 0;
            foreach (var node in phase.Graph.Nodes)
            {
                if (node == null) throw new InvalidOperationException($"Phase '{phase.Id}' contains a null node row.");
                if (string.IsNullOrEmpty(node.Id)) throw new InvalidOperationException($"Phase '{phase.Id}' has a node with no id.");
                if (node is PhaseEntryNodeDefinition) entryCount++;
                _nodesById[node.Id] = node;

                foreach (var output in node.Outputs)
                {
                    if (output == null || string.IsNullOrEmpty(output.Id))
                    {
                        throw new InvalidOperationException($"Node '{node.Id}' of phase '{phase.Id}' has a null/empty output.");
                    }
                    _outputsById[output.Id] = output;
                }
            }

            if (entryCount != 1)
            {
                throw new InvalidOperationException($"Phase '{phase.Id}' must have exactly one Entry node (found {entryCount}).");
            }

            foreach (var edge in phase.Graph.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.SourceOutputId) || string.IsNullOrEmpty(edge.TargetNodeId))
                {
                    throw new InvalidOperationException($"Phase '{phase.Id}' has a malformed edge row.");
                }
                if (!_outputsById.ContainsKey(edge.SourceOutputId))
                {
                    throw new InvalidOperationException($"Phase '{phase.Id}' edge '{edge.Id}' references unknown output '{edge.SourceOutputId}'.");
                }
                if (!_nodesById.ContainsKey(edge.TargetNodeId))
                {
                    throw new InvalidOperationException($"Phase '{phase.Id}' edge '{edge.Id}' references unknown target node '{edge.TargetNodeId}'.");
                }
                _edgeFromOutput[edge.SourceOutputId] = edge.TargetNodeId;
            }
        }

        /// <summary>
        /// Steps the run's graph until yield/transfer/complete/error. At most one
        /// Card is executed per call (the Advance budget); post-card automatic
        /// work continues until the next CardExecutor boundary.
        /// </summary>
        public async Task<PhaseAdvanceResult> AdvanceAsync(PhaseRun run, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (run.PhaseId != _phase.Id)
            {
                throw new InvalidOperationException($"PhaseGraphVm for '{_phase.Id}' cannot advance run of '{run.PhaseId}'.");
            }

            try
            {
                return await AdvanceCoreAsync(run, context, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                // Structural/authoring failures during stepping surface as loud
                // runtime errors (event + result), never as silent repair.
                return Fail(run, ex.Message);
            }
        }

        private async Task<PhaseAdvanceResult> AdvanceCoreAsync(PhaseRun run, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            var cardBudget = 1;
            var node = run.CurrentNode;
            CardDefinition executedCard = null;

            // Fresh run: enter the Entry node.
            if (node == null)
            {
                PhaseEntered?.Invoke(_phase.Id);
                node = FindEntry();
                run.CurrentNode = node;
                PhaseNodeChanged?.Invoke(node.Id);
                node = FollowNormal(run, node);
            }

            var steps = 0;
            while (true)
            {
                if (++steps > MaxStepsPerAdvance)
                {
                    return Fail(run, $"loop guard: exceeded {MaxStepsPerAdvance} node steps in one Advance " +
                                    $"(phase '{_phase.Id}', node '{node?.Id}', step {steps}). No silent gameplay loop.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                switch (node)
                {
                    case CardExecutorNodeDefinition cardExecutor:
                    {
                        if (cardBudget == 0)
                        {
                            // The card budget is spent; park at this executor. If a
                            // card ran this Advance, that is the user-paced result.
                            run.CurrentNode = cardExecutor;
                            return executedCard != null
                                ? PhaseAdvanceResult.CardExecuted(executedCard)
                                : PhaseAdvanceResult.YieldedForCard;
                        }

                        var selected = DrawCard(run);
                        if (selected == null)
                        {
                            return Fail(run, $"no eligible card for phase '{_phase.Id}' (tags: include [{Join(_phase.MustIncludeTags)}], exclude [{Join(_phase.MustExcludeTags)}]).");
                        }

                        CardStarted?.Invoke(selected);
                        var cardContext = ContextFor(context, run, ActionOwnerScope.CardSequence);
                        var cardResult = await _executor.ExecuteSequenceAsync(selected.Sequence, cardContext, cancellationToken);
                        CardFinished?.Invoke(selected);
                        executedCard = selected;

                        if (cardResult.Transfer != ActionTransfer.None)
                        {
                            run.CurrentNode = cardExecutor;
                            return PhaseAdvanceResult.Transferred(cardResult);
                        }

                        cardBudget--;
                        node = FollowNormal(run, cardExecutor);
                        break;
                    }

                    case VariableCheckNodeDefinition check:
                    {
                        var (value, result) = Evaluate(check, run, context);
                        VariableCheckEvaluated?.Invoke(check, value, result);
                        node = result ? Follow(run, check, GraphPortKind.True) : Follow(run, check, GraphPortKind.False);
                        break;
                    }

                    case ActionNodeDefinition action:
                    {
                        var actionContext = ContextFor(context, run, ActionOwnerScope.PhaseActionSequence);
                        var actionResult = await _executor.ExecuteSequenceAsync(action.Sequence, actionContext, cancellationToken);
                        if (actionResult.Transfer != ActionTransfer.None)
                        {
                            run.CurrentNode = action;
                            return PhaseAdvanceResult.Transferred(actionResult);
                        }
                        node = FollowNormal(run, action);
                        break;
                    }

                    case PhaseDecisionNodeDefinition decision:
                    {
                        var selectedOption = await PromptForDecisionAsync(decision, context, cancellationToken);
                        if (selectedOption != null)
                        {
                            var optionContext = ContextFor(context, run, ActionOwnerScope.PhaseActionSequence);
                            var optionResult = await _executor.ExecuteSequenceAsync(selectedOption.Sequence, optionContext, cancellationToken);
                            if (optionResult.Transfer != ActionTransfer.None)
                            {
                                run.CurrentNode = decision;
                                return PhaseAdvanceResult.Transferred(optionResult);
                            }
                        }
                        node = FollowNormal(run, decision);
                        break;
                    }

                    case ReturnNodeDefinition returnNodeInstance:
                        run.CurrentNode = returnNodeInstance;
                        return PhaseAdvanceResult.Transferred(ActionExecutionResult.ReturnTransfer);

                    case PhaseEntryNodeDefinition entryNode:
                        node = FollowNormal(run, entryNode);
                        break;

                    default:
                        return Fail(run, $"unknown phase node type '{node.GetType().Name}' at '{node.Id}'.");
                }
            }
        }

        // ---------- node following ----------

        private GraphNodeDefinition Follow(PhaseRun run, GraphNodeDefinition node, GraphPortKind kind)
        {
            foreach (var output in node.Outputs)
            {
                if (output.Kind == kind)
                {
                    return FollowOutput(run, node, output);
                }
            }
            throw new InvalidOperationException(
                $"Phase '{_phase.Id}': node '{node.Id}' has no '{kind}' output to follow.");
        }

        private GraphNodeDefinition FollowNormal(PhaseRun run, GraphNodeDefinition node)
        {
            return Follow(run, node, GraphPortKind.Normal);
        }

        private GraphNodeDefinition FollowOutput(PhaseRun run, GraphNodeDefinition node, GraphOutputDefinition output)
        {
            if (!_edgeFromOutput.TryGetValue(output.Id, out var targetId))
            {
                throw new InvalidOperationException(
                    $"Phase '{_phase.Id}': node '{node.Id}' output '{output.Id}' is a dead end with no outgoing edge.");
            }
            var target = _nodesById[targetId];
            run.CurrentNode = target;
            PhaseNodeChanged?.Invoke(target.Id);
            return target;
        }

        // ---------- card selection ----------

        private CardDefinition DrawCard(PhaseRun run)
        {
            if (!_cardSelector.TryDrawCard(_phase.MustIncludeTags, _phase.MustExcludeTags, run.CardRng, out var card))
            {
                return null;
            }
            run.DrawHistory.Add(card.Id);
            return card;
        }

        // ---------- variable check ----------

        private (float Value, bool Result) Evaluate(VariableCheckNodeDefinition check, PhaseRun run, ActionExecutionContext context)
        {
            float value;
            switch (check.SourceKind)
            {
                case VariableSourceKind.PhaseProgress:
                    value = run.Progress.Value;
                    break;
                case VariableSourceKind.Temperature:
                    value = context.Temperatures.Get(check.VariableKey);
                    break;
                case VariableSourceKind.Stat:
                    value = context.Player.Stats.Get(check.VariableKey);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown variable source kind '{check.SourceKind}'.");
            }

            var compare = check.CompareValue;
            bool result;
            switch (check.Operator)
            {
                case VariableCompareOperator.LessThan: result = value < compare; break;
                case VariableCompareOperator.LessThanOrEqual: result = value <= compare; break;
                case VariableCompareOperator.Equal: result = value == compare; break;
                case VariableCompareOperator.NotEqual: result = value != compare; break;
                case VariableCompareOperator.GreaterThanOrEqual: result = value >= compare; break;
                case VariableCompareOperator.GreaterThan: result = value > compare; break;
                default:
                    throw new InvalidOperationException($"Unknown compare operator '{check.Operator}'.");
            }
            return (value, result);
        }

        // ---------- decision ----------

        private async Task<PhaseDecisionOptionDefinition> PromptForDecisionAsync(
            PhaseDecisionNodeDefinition decision, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (context.Services.Prompts == null)
            {
                _services.Log.Warning($"PhaseDecision '{decision.Id}' has no prompt service; continuing without a choice.");
                return null;
            }

            var labels = new List<string>();
            foreach (var option in decision.Options) labels.Add(option.Label);

            var selectedIndex = await context.Services.Prompts.AskAsync(decision.Prompt, labels, cancellationToken);
            if (selectedIndex == null || selectedIndex < 0 || selectedIndex >= decision.Options.Count)
            {
                return null;
            }
            return decision.Options[selectedIndex.Value];
        }

        // ---------- helpers ----------

        private PhaseAdvanceResult Fail(PhaseRun run, string message)
        {
            RuntimeError?.Invoke(message);
            return PhaseAdvanceResult.Error(message);
        }

        private PhaseEntryNodeDefinition FindEntry()
        {
            foreach (var node in _phase.Graph.Nodes)
            {
                if (node is PhaseEntryNodeDefinition entry) return entry;
            }
            throw new InvalidOperationException($"Phase '{_phase.Id}' has no Entry node.");
        }

        private static ActionExecutionContext ContextFor(ActionExecutionContext baseContext, PhaseRun run, ActionOwnerScope scope)
        {
            return new ActionExecutionContext(
                baseContext.Player,
                baseContext.Services,
                baseContext.Catalog,
                baseContext.Temperatures,
                run.Progress,
                scope);
        }

        private static string Join(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return "";
            return string.Join(",", values);
        }
    }
}
