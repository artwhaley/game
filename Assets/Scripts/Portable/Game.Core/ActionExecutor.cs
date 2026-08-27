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
        /// Runs an owned sequence in order. Returns the first transfer request a
        /// flow-control instance produces; later instances are NOT discarded on a
        /// transfer (the VM saves the next index and resumes on RETURN).
        /// </summary>
        public async Task<ActionExecutionResult> ExecuteSequenceAsync(
            ActionSequenceDefinition sequence, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));

            for (var index = 0; index < sequence.Instances.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var instance = sequence.Instances[index];
                var result = await ExecuteInstanceAsync(instance, context, cancellationToken);
                if (result.Transfer != ActionTransfer.None)
                {
                    return result;
                }
            }

            return ActionExecutionResult.Continue;
        }

        public async Task<ActionExecutionResult> ExecuteInstanceAsync(
            ActionInstanceDefinition instance, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var info = ActionTypeRegistry.ForInstance(instance);
            ActionTypeRegistry.ValidateScope(instance, context.ActiveScope);

            if (info.IsAlwaysBlocking && !instance.IsBlocking)
            {
                throw new InvalidOperationException(
                    $"Flow-control action '{instance.Id}' ({info.TypeKey}) must be blocking.");
            }

            if (info.IsAlwaysBlocking)
            {
                // Transfer mechanics land with the graph VM; reduce to the request now.
                return ReduceFlow(instance, info.TypeKey);
            }

            if (!instance.IsBlocking)
            {
                _background.Start(ExecuteAsync(instance, context, cancellationToken));
                return ActionExecutionResult.Continue;
            }

            return await ExecuteAsync(instance, context, cancellationToken);
        }

        private async Task<ActionExecutionResult> ExecuteAsync(ActionInstanceDefinition instance, ActionExecutionContext context, CancellationToken cancellationToken)
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
                    return await ExecutePromptChoiceAsync(choice, context, cancellationToken);

                default:
                    throw new InvalidOperationException(
                        $"ActionExecutor: unsupported general action type '{instance.GetType().Name}'.");
            }
        }

        private async Task<ActionExecutionResult> ExecutePromptChoiceAsync(
            PromptChoiceInstanceDefinition choice, ActionExecutionContext context, CancellationToken cancellationToken)
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

            // The option sequence inherits the enclosing execution context and scope.
            return await ExecuteSequenceAsync(selected.Sequence, context, cancellationToken);
        }

        private static ActionExecutionResult ReduceFlow(ActionInstanceDefinition instance, string typeKey)
        {
            switch (instance)
            {
                case PhaseGotoInstanceDefinition phaseGoto:
                    return ActionExecutionResult.PhaseGoto(phaseGoto.PhaseExitId);
                case SessionGotoInstanceDefinition sessionGoto:
                    return ActionExecutionResult.SessionGoto(sessionGoto.Label);
                case ReturnInstanceDefinition returnInstance:
                    return ActionExecutionResult.ReturnTransfer;
                case EndSessionInstanceDefinition endSession:
                    return ActionExecutionResult.EndSessionTransfer;
                default:
                    throw new InvalidOperationException(
                        $"ActionExecutor: unknown flow action type '{typeKey}'.");
            }
        }
    }
}
