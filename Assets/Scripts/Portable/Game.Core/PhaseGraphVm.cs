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
    /// dictionaries built once. Execution: Entry→normal; CardExecutor→continuous
    /// card selection/execution until an authored yield or transfer; VariableCheck→
    /// True/False from live state; ActionNode→sequence then normal; PhaseDecision→
    /// prompt + selected option sequence then common normal; ReturnNode→Return.
    /// A deterministic node/action ceiling per RunUntilYield catches non-yield
    /// cycles with a loud, identifiable error.
    /// </summary>
    public sealed class PhaseGraphVm
    {
        /// <summary>Default shared safety ceiling per RunUntilYield call.</summary>
        public const int DefaultExecutionBudget = 10000;

        /// <summary>Canonical Happiness temperature id (weighting input; 50 when absent).</summary>
        public const string HappinessTemperatureId = "happiness";

        private readonly ContentCatalog _catalog;
        private readonly CoreServices _services;
        private readonly BackgroundActionTracker _tracker;
        private readonly ActionExecutor _executor;
        private readonly CardSelector _cardSelector;
        private readonly CardSelectionProfile _selectionProfile;
        private readonly SessionCardWeightingDefinition _sessionWeighting;
        private readonly string _sessionId;
        private readonly PhaseDefinition _phase;
        private readonly int _executionBudget;

        private readonly Dictionary<string, GraphNodeDefinition> _nodesById;
        private readonly Dictionary<string, GraphOutputDefinition> _outputsById;
        private readonly Dictionary<string, string> _edgeFromOutput; // outputId -> targetNodeId

        public event Action<string> PhaseEntered;      // phase id
        public event Action<string> PhaseNodeChanged;  // node id
        public event Action<CardDefinition> CardStarted;
        public event Action<CardDefinition> CardFinished;
        public event Action<VariableCheckNodeDefinition, float, bool> VariableCheckEvaluated;
        public event Action<string> RuntimeError;      // message

        public event Action<CardSelector.SelectionResult> CardSelectionEvaluated;

        public PhaseGraphVm(GameContentDefinition content, PhaseDefinition phase, CoreServices services,
            BackgroundActionTracker tracker, int executionBudget = DefaultExecutionBudget,
            CardSelectionProfile selectionProfile = null, SessionCardWeightingDefinition sessionWeighting = null,
            string sessionId = null)
        {
            _catalog = new ContentCatalog(content);
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _executor = new ActionExecutor(_tracker);
            _cardSelector = new CardSelector(content, _catalog);
            _selectionProfile = selectionProfile ?? new CardSelectionProfile();
            _sessionWeighting = sessionWeighting ?? new SessionCardWeightingDefinition();
            _sessionId = sessionId ?? "";
            _phase = phase ?? throw new ArgumentNullException(nameof(phase));
            if (executionBudget <= 0) throw new ArgumentOutOfRangeException(nameof(executionBudget));
            _executionBudget = executionBudget;

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
        /// Runs the graph until an authored yield/transfer, completion, or error.
        /// CardFinished is not a pause boundary; ordinary cards continue until
        /// their action sequences explicitly yield or the graph transfers.
        /// </summary>
        public Task<PhaseAdvanceResult> AdvanceAsync(PhaseRun run, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            return RunUntilYieldAsync(run, context, cancellationToken);
        }

        /// <summary>Runs continuously until an authored wait, transfer, completion, or error.</summary>
        public async Task<PhaseAdvanceResult> RunUntilYieldAsync(PhaseRun run, ActionExecutionContext context,
            CancellationToken cancellationToken, GraphExecutionBudget budget = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (run.PhaseId != _phase.Id)
            {
                throw new InvalidOperationException($"PhaseGraphVm for '{_phase.Id}' cannot advance run of '{run.PhaseId}'.");
            }

            try
            {
                budget = budget ?? new GraphExecutionBudget(_executionBudget);
                if (run.SuspendedCard != null)
                {
                    return await ResumeCardAsync(run, context, cancellationToken, budget);
                }
                if (run.SuspendedActionLocus != null)
                {
                    return await ResumeActionAsync(run, context, cancellationToken, budget);
                }
                return await StepLoopAsync(run, context, cancellationToken, budget,
                    run.CurrentNode, freshEntry: run.CurrentNode == null);
            }
            catch (InvalidOperationException ex)
            {
                // Structural/authoring failures during stepping surface as loud
                // runtime errors (event + result), never as silent repair.
                return Fail(run, ex.Message);
            }
        }

        /// <summary>
        /// Resumes a suspended run at a saved graph locus (RETURN mechanics):
        /// executes the saved action chain (innermost first) and, on normal
        /// completion, continues stepping from the locus's normal edge with a
        /// fresh card budget. A transfer during the chain re-saves the remaining
        /// points.
        /// </summary>
        public async Task<PhaseAdvanceResult> ResumeFromContinuationAsync(
            PhaseRun run, GraphNodeDefinition locus, IReadOnlyList<ContinuationPoint> chain,
            ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget = null, CardDefinition interruptedCard = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (locus == null) throw new ArgumentNullException(nameof(locus));

            try
            {
                budget = budget ?? new GraphExecutionBudget(_executionBudget);
                run.CurrentNode = locus;
                PhaseNodeChanged?.Invoke(locus.Id);

                if (interruptedCard != null)
                {
                    return await ResumeCardChainAsync(run, locus, interruptedCard, chain, context, cancellationToken, budget);
                }

                if (chain != null && chain.Count > 0)
                {
                    var scope = ScopeForLocus(locus);
                    var resumeContext = ContextFor(context, run, scope);
                    var resumeResult = await _executor.ResumeChainAsync(chain, resumeContext, cancellationToken, budget);
                    if (resumeResult.Transfer == ActionTransfer.WaitForContinue)
                    {
                        run.SuspendedActionLocus = locus;
                        run.SuspendedActionChain = resumeResult.Continuation;
                        return PhaseAdvanceResult.YieldedForContinue;
                    }
                    if (resumeResult.Transfer != ActionTransfer.None)
                    {
                        run.CurrentNode = locus;
                        return PhaseAdvanceResult.Transferred(resumeResult);
                    }
                }

                var next = FollowNormal(run, locus);
                return await StepLoopAsync(run, context, cancellationToken, budget, next, freshEntry: false);
            }
            catch (InvalidOperationException ex)
            {
                return Fail(run, ex.Message);
            }
        }

        private static ActionOwnerScope ScopeForLocus(GraphNodeDefinition locus)
        {
            if (locus is CardExecutorNodeDefinition) return ActionOwnerScope.CardSequence;
            return ActionOwnerScope.PhaseActionSequence;
        }

        private async Task<PhaseAdvanceResult> StepLoopAsync(
            PhaseRun run, ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget, GraphNodeDefinition initialNode, bool freshEntry)
        {
            var node = initialNode;

            // Fresh run: enter the Entry node.
            if (freshEntry)
            {
                PhaseEntered?.Invoke(_phase.Id);
                node = FindEntry();
                run.CurrentNode = node;
                PhaseNodeChanged?.Invoke(node.Id);
                node = FollowNormal(run, node);
            }

            while (true)
            {
                budget.Consume("phase node", phaseId: _phase.Id, nodeId: node?.Id);

                cancellationToken.ThrowIfCancellationRequested();

                switch (node)
                {
                    case CardExecutorNodeDefinition cardExecutor:
                    {
                        CardDefinition selected;
                        try
                        {
                            selected = DrawCard(run, context);
                        }
                        catch (NoEligibleCardException ex)
                        {
                            // Loud typed failure — never skip, relax, or alter flow.
                            return Fail(run, ex.Message);
                        }

                        CardStarted?.Invoke(selected);
                        var cardContext = ContextFor(context, run, ActionOwnerScope.CardSequence);
                        var cardResult = await _executor.ExecuteSequenceAsync(selected.Sequence, cardContext, cancellationToken, budget);

                        if (cardResult.Transfer == ActionTransfer.WaitForContinue)
                        {
                            run.CurrentNode = cardExecutor;
                            run.SuspendedCard = selected;
                            run.SuspendedCardChain = cardResult.Continuation;
                            return PhaseAdvanceResult.YieldedForContinue;
                        }

                        if (cardResult.Transfer != ActionTransfer.None)
                        {
                            run.CurrentNode = cardExecutor;
                            cardResult.InterruptedCard = selected;
                            return PhaseAdvanceResult.Transferred(cardResult);
                        }

                        CardFinished?.Invoke(selected);
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
                        var actionResult = await _executor.ExecuteSequenceAsync(action.Sequence, actionContext, cancellationToken, budget);
                        if (actionResult.Transfer == ActionTransfer.WaitForContinue)
                        {
                            run.CurrentNode = action;
                            run.SuspendedActionLocus = action;
                            run.SuspendedActionChain = actionResult.Continuation;
                            return PhaseAdvanceResult.YieldedForContinue;
                        }
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
                            var optionResult = await _executor.ExecuteSequenceAsync(selectedOption.Sequence, optionContext, cancellationToken, budget);
                            if (optionResult.Transfer == ActionTransfer.WaitForContinue)
                            {
                                run.CurrentNode = decision;
                                run.SuspendedActionLocus = decision;
                                run.SuspendedActionChain = optionResult.Continuation;
                                return PhaseAdvanceResult.YieldedForContinue;
                            }
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

        private Task<PhaseAdvanceResult> ResumeCardAsync(PhaseRun run, ActionExecutionContext context,
            CancellationToken cancellationToken, GraphExecutionBudget budget)
        {
            var card = run.SuspendedCard;
            var chain = run.SuspendedCardChain;
            run.SuspendedCard = null;
            run.SuspendedCardChain = null;
            return ResumeCardChainAsync(run, run.CurrentNode, card, chain, context, cancellationToken, budget);
        }

        private async Task<PhaseAdvanceResult> ResumeCardChainAsync(PhaseRun run, GraphNodeDefinition locus,
            CardDefinition card, IReadOnlyList<ContinuationPoint> chain, ActionExecutionContext context,
            CancellationToken cancellationToken, GraphExecutionBudget budget)
        {
            if (!(locus is CardExecutorNodeDefinition))
            {
                throw new InvalidOperationException(
                    $"Phase '{_phase.Id}' cannot resume Card '{card?.Id}' from non-CardExecutor locus '{locus?.Id}'.");
            }

            var cardContext = ContextFor(context, run, ActionOwnerScope.CardSequence);
            var result = await _executor.ResumeChainAsync(
                chain ?? Array.Empty<ContinuationPoint>(), cardContext, cancellationToken, budget);
            if (result.Transfer == ActionTransfer.WaitForContinue)
            {
                run.CurrentNode = locus;
                run.SuspendedCard = card;
                run.SuspendedCardChain = result.Continuation;
                return PhaseAdvanceResult.YieldedForContinue;
            }
            if (result.Transfer != ActionTransfer.None)
            {
                run.CurrentNode = locus;
                result.InterruptedCard = card;
                return PhaseAdvanceResult.Transferred(result);
            }

            run.SuspendedCard = null;
            run.SuspendedCardChain = null;
            CardFinished?.Invoke(card);
            var next = FollowNormal(run, locus);
            return await StepLoopAsync(run, context, cancellationToken, budget, next, freshEntry: false);
        }

        private async Task<PhaseAdvanceResult> ResumeActionAsync(PhaseRun run, ActionExecutionContext context,
            CancellationToken cancellationToken, GraphExecutionBudget budget)
        {
            var locus = run.SuspendedActionLocus;
            var chain = run.SuspendedActionChain;
            run.SuspendedActionLocus = null;
            run.SuspendedActionChain = null;

            var actionContext = ContextFor(context, run, ScopeForLocus(locus));
            var result = await _executor.ResumeChainAsync(
                chain ?? Array.Empty<ContinuationPoint>(), actionContext, cancellationToken, budget);
            if (result.Transfer == ActionTransfer.WaitForContinue)
            {
                run.CurrentNode = locus;
                run.SuspendedActionLocus = locus;
                run.SuspendedActionChain = result.Continuation;
                return PhaseAdvanceResult.YieldedForContinue;
            }
            if (result.Transfer != ActionTransfer.None)
            {
                run.CurrentNode = locus;
                return PhaseAdvanceResult.Transferred(result);
            }

            var next = FollowNormal(run, locus);
            return await StepLoopAsync(run, context, cancellationToken, budget, next, freshEntry: false);
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

        private CardDefinition DrawCard(PhaseRun run, ActionExecutionContext context)
        {
            var happiness = 50f;
            try
            {
                happiness = context.Temperatures.Get(HappinessTemperatureId);
            }
            catch (InvalidOperationException)
            {
                // No Happiness temperature authored: selection proceeds at 50.
            }

            var evaluation = _cardSelector.Evaluate(_phase, _selectionProfile, _sessionWeighting, happiness, _sessionId);
            var selected = _cardSelector.Draw(evaluation, run.CardRng);
            CardSelectionEvaluated?.Invoke(evaluation);
            run.DrawHistory.Add(selected.Id);
            return selected;
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
                scope,
                baseContext.DialogRng);
        }
    }
}
