using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Executes Action Instances against an <see cref="ActionExecutionContext"/>.
    /// General instances run their parameterized behavior here; flow-control
    /// instances are validated (scope + always-blocking) and reduced to a
    /// transfer request the graph VM performs (Tickets 08-09).
    ///
    /// Nonblocking instances are started on the context's background tracker and
    /// never block sequence continuation; blocking instances are awaited.
    /// Progress changes happen ONLY through IncrementProgress instances — there
    /// is no implicit automatic increment anywhere in Core.
    /// </summary>
    public sealed class ActionExecutor
    {
        private readonly BackgroundActionTracker _background;

        public ActionExecutor(BackgroundActionTracker background)
        {
            _background = background ?? throw new ArgumentNullException(nameof(background));
        }

        /// <summary>
        /// Runs an owned sequence from index 0. Returns the first transfer request
        /// a flow-control instance produces; later instances are NOT discarded on a
        /// transfer — the VM saves the next index (plus any enclosing sequence
        /// points) and resumes on RETURN.
        /// </summary>
        public Task<ActionExecutionResult> ExecuteSequenceAsync(
            ActionSequenceDefinition sequence, ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget = null)
        {
            return ExecuteSequenceFromAsync(sequence, 0, context, cancellationToken, budget ?? new GraphExecutionBudget());
        }

        /// <summary>
        /// Resumes an owned sequence at a saved index (RETURN mechanics). A nested
        /// chain resumes inner sequence first, then the outer sequence continues at
        /// its own saved point; only then does the caller follow the graph locus.
        /// </summary>
        public async Task<ActionExecutionResult> ResumeChainAsync(
            IReadOnlyList<ContinuationPoint> chain, ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget = null)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            budget = budget ?? new GraphExecutionBudget();

            // Innermost first: resume the deepest saved sequence point, then
            // cascade outward through the enclosing sequences.
            for (var i = 0; i < chain.Count; i++)
            {
                var point = chain[i];
                var result = await ExecuteSequenceFromAsync(point.Sequence, point.NextActionIndex, context, cancellationToken, budget);
                if (result.Transfer != ActionTransfer.None)
                {
                    // A nested transfer mid-resume re-saves the remaining points.
                    for (var j = i + 1; j < chain.Count; j++)
                    {
                        var remaining = chain[j];
                        result.Continuation.Add(new ContinuationPoint(remaining.Sequence, remaining.NextActionIndex));
                    }
                    return result;
                }
            }

            return ActionExecutionResult.Continue;
        }

        private async Task<ActionExecutionResult> ExecuteSequenceFromAsync(
            ActionSequenceDefinition sequence, int startIndex, ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));

            for (var index = startIndex; index < sequence.Instances.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                budget.Consume("action", nodeId: sequence.Id);
                var instance = sequence.Instances[index];
                var result = await ExecuteInstanceAsync(instance, context, cancellationToken, budget);
                if (result.Transfer != ActionTransfer.None)
                {
                    // Save this sequence's resume point after the transferring
                    // instance. Nested option sequences already recorded their
                    // inner points (innermost first), so append outward.
                    result.Continuation.Add(new ContinuationPoint(sequence, index + 1));
                    return result;
                }
            }

            return ActionExecutionResult.Continue;
        }

        public async Task<ActionExecutionResult> ExecuteInstanceAsync(
            ActionInstanceDefinition instance, ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget = null)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (context == null) throw new ArgumentNullException(nameof(context));
            budget = budget ?? new GraphExecutionBudget();

            var info = ActionTypeRegistry.ForInstance(instance);
            ActionTypeRegistry.ValidateScope(instance, context.ActiveScope);
            context.Services.Log.Info($"ACTION START {info.DisplayLabel} [{instance.Id}]");

            if (info.IsAlwaysBlocking && !instance.IsBlocking)
            {
                throw new InvalidOperationException(
                    $"Flow-control action '{instance.Id}' ({info.TypeKey}) must be blocking.");
            }

            if (info.IsAlwaysBlocking)
            {
                // Transfer mechanics land with the graph VM; reduce to the request now.
                var flow = ReduceFlow(instance, info.TypeKey);
                context.Services.Log.Info($"ACTION FINISHED {info.DisplayLabel} [{instance.Id}] -> {flow.Transfer}");
                return flow;
            }

            if (!instance.IsBlocking)
            {
                _background.Start(ExecuteNonBlockingWithDiagnosticsAsync(instance, info, context, cancellationToken, budget));
                return ActionExecutionResult.Continue;
            }

            try
            {
                var result = await ExecuteAsync(instance, context, cancellationToken, budget);
                context.Services.Log.Info($"ACTION FINISHED {info.DisplayLabel} [{instance.Id}]");
                return result;
            }
            catch (Exception ex)
            {
                context.Services.Log.Error($"ACTION FAILED {info.DisplayLabel} [{instance.Id}]: {ex.Message}");
                throw;
            }
        }

        private async Task ExecuteNonBlockingWithDiagnosticsAsync(ActionInstanceDefinition instance, ActionTypeInfo info,
            ActionExecutionContext context, CancellationToken cancellationToken, GraphExecutionBudget budget)
        {
            try
            {
                await ExecuteAsync(instance, context, cancellationToken, budget);
                context.Services.Log.Info($"ACTION FINISHED {info.DisplayLabel} [{instance.Id}] (nonblocking)");
            }
            catch (Exception ex)
            {
                context.Services.Log.Error($"ACTION FAILED {info.DisplayLabel} [{instance.Id}]: {ex.Message}");
                context.Services.Log.Error($"Background action faulted [{instance.Id}]: {ex.Message}");
            }
        }

        private async Task<ActionExecutionResult> ExecuteAsync(ActionInstanceDefinition instance, ActionExecutionContext context,
            CancellationToken cancellationToken, GraphExecutionBudget budget)
        {
            switch (instance)
            {
                case DebugInstanceDefinition debug:
                    context.Services.Log.Info(debug.Message);
                    if (debug.DelaySeconds > 0f)
                    {
                        await context.Services.Delay.DelayAsync(
                            TimeSpan.FromSeconds(debug.DelaySeconds), cancellationToken);
                    }
                    return ActionExecutionResult.Continue;

                case StatIncreaseInstanceDefinition stat:
                    context.Player.Stats.Add(stat.StatKey, (int)stat.Amount);
                    return ActionExecutionResult.Continue;

                case IncrementProgressInstanceDefinition progress:
                    if (context.PhaseProgress == null)
                    {
                        throw new InvalidOperationException(
                            $"IncrementProgress action '{instance.Id}' ran with no active PhaseRun.");
                    }
                    context.PhaseProgress.Value += progress.Amount;
                    return ActionExecutionResult.Continue;

                case ModifyTemperatureInstanceDefinition temperature:
                    context.Temperatures.Add(temperature.TemperatureId, temperature.Amount);
                    return ActionExecutionResult.Continue;

                case CutsceneInstanceDefinition cutscene:
                    if (context.Services.Cutscene == null)
                    {
                        context.Services.Log.Warning(
                            $"Cutscene action '{instance.Id}' has no cutscene service; logging only.");
                        return ActionExecutionResult.Continue;
                    }
                    await context.Services.Cutscene.PlayAsync(cutscene.ResourceId, cancellationToken);
                    return ActionExecutionResult.Continue;

                case PromptChoiceInstanceDefinition choice:
                    return await ExecutePromptChoiceAsync(choice, context, cancellationToken, budget);

                default:
                    throw new InvalidOperationException(
                        $"ActionExecutor: unsupported general action type '{instance.GetType().Name}'.");
            }
        }

        private async Task<ActionExecutionResult> ExecutePromptChoiceAsync(
            PromptChoiceInstanceDefinition choice, ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget)
        {
            if (context.Services.Prompts == null)
            {
                context.Services.Log.Warning(
                    $"PromptChoice action '{choice.Id}' has no prompt service; skipping choice.");
                return ActionExecutionResult.Continue;
            }

            var labels = new List<string>();
            foreach (var option in choice.Options) labels.Add(option.Label);

            var selectedIndex = await context.Services.Prompts.AskAsync(choice.Prompt, labels, cancellationToken);
            if (selectedIndex == null || selectedIndex < 0 || selectedIndex >= choice.Options.Count)
            {
                return ActionExecutionResult.Continue;
            }

            var selected = choice.Options[selectedIndex.Value];
            if (selected.Sequence == null || selected.Sequence.Instances.Count == 0)
            {
                return ActionExecutionResult.Continue;
            }

            // PromptChoice owns a nested sequence. It inherits the enclosing
            // runtime state, but a SessionDecision option's nested sequence is
            // not itself a direct SessionDecision option: SessionGoto must be
            // rejected at every nested depth instead of gaining the containing
            // decision's projected socket ownership.
            var nestedScope = ActionOwnerScopes.NestedPromptChoice(context.ActiveScope);
            var nestedContext = new ActionExecutionContext(
                context.Player,
                context.Services,
                context.Catalog,
                context.Temperatures,
                context.PhaseProgress,
                nestedScope);
            return await ExecuteSequenceAsync(selected.Sequence, nestedContext, cancellationToken, budget);
        }

        private static ActionExecutionResult ReduceFlow(ActionInstanceDefinition instance, string typeKey)
        {
            switch (instance)
            {
                case PhaseGotoInstanceDefinition phaseGoto:
                    return ActionExecutionResult.PhaseGoto(phaseGoto.PhaseExitId);
                case SessionGotoInstanceDefinition sessionGoto:
                    return ActionExecutionResult.SessionGoto(sessionGoto.Id, sessionGoto.Label);
                case ReturnInstanceDefinition returnInstance:
                    return ActionExecutionResult.ReturnTransfer;
                case EndSessionInstanceDefinition endSession:
                    return ActionExecutionResult.EndSessionTransfer;
                case WaitForContinueInstanceDefinition wait:
                    return ActionExecutionResult.WaitForContinueTransfer;
                default:
                    throw new InvalidOperationException(
                        $"ActionExecutor: unknown flow action type '{typeKey}'.");
            }
        }
    }
}
