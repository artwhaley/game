using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Executes card action definitions with baseline semantics:
    /// - null actions are skipped;
    /// - blocking actions are awaited in order;
    /// - nonblocking actions are started through the background tracker and
    ///   execution continues immediately.
    /// Misconfiguration (missing prompt/cutscene service, empty options,
    /// dismissed choice, missing resource) logs an error and no-ops, exactly
    /// as the current Unity implementation does. Unexpected exceptions fault
    /// the task and must be observed by the caller or the tracker.
    /// </summary>
    public sealed class ActionExecutor
    {
        private readonly BackgroundActionTracker _tracker;

        public ActionExecutor(BackgroundActionTracker tracker)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }

        /// <summary>Runs one card's actions in order.</summary>
        public async Task ExecuteCardAsync(CardDefinition card, GameContext context, CancellationToken cancellationToken)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            var actions = card.Actions;
            if (actions == null) return;

            for (var i = 0; i < actions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var action = actions[i];
                if (action == null) continue;

                if (action.IsBlocking)
                {
                    await ExecuteActionAsync(action, context, cancellationToken);
                }
                else
                {
                    _tracker.Start(ExecuteActionAsync(action, context, cancellationToken));
                }
            }
        }

        /// <summary>Runs a single action to completion (used for blocking actions and chosen children).</summary>
        public async Task ExecuteActionAsync(GameActionDefinition action, GameContext context, CancellationToken cancellationToken)
        {
            switch (action)
            {
                case DebugActionDefinition debug:
                    await ExecuteDebugAsync(debug, context, cancellationToken);
                    break;
                case StatIncreaseActionDefinition stat:
                    ExecuteStat(stat, context);
                    break;
                case ChoiceActionDefinition choice:
                    await ExecuteChoiceAsync(choice, context, cancellationToken);
                    break;
                case CutsceneActionDefinition cutscene:
                    await ExecuteCutsceneAsync(cutscene, context, cancellationToken);
                    break;
                default:
                    context.Services.Log.Error($"[TruthCardGame] No executor for action type {action.GetType().Name}.");
                    break;
            }
        }

        private async Task ExecuteDebugAsync(DebugActionDefinition action, GameContext context, CancellationToken ct)
        {
            // Baseline order: log first, then wait.
            context.Services.Log.Info($"[TruthCardGame] {context.Player.Name}: {action.Message}");
            if (action.DelaySeconds > 0f)
            {
                await context.Services.Delay.DelayAsync(TimeSpan.FromSeconds(action.DelaySeconds), ct);
            }
        }

        private void ExecuteStat(StatIncreaseActionDefinition action, GameContext context)
        {
            context.Player.Stats.Add(action.StatKey, action.Amount);
            context.Services.Log.Info($"[TruthCardGame] {context.Player.Name} {action.StatKey} +{action.Amount} (now {context.Player.Stats.Get(action.StatKey)})");
        }

        private async Task ExecuteChoiceAsync(ChoiceActionDefinition action, GameContext context, CancellationToken ct)
        {
            var prompts = context.Services.Prompts;
            if (prompts == null)
            {
                context.Services.Log.Error("[TruthCardGame] ChoiceAction: no prompt service available.");
                return;
            }
            if (action.Options == null || action.Options.Count == 0)
            {
                context.Services.Log.Error("[TruthCardGame] ChoiceAction: no options configured.");
                return;
            }

            var labels = new List<string>(action.Options.Count);
            foreach (var option in action.Options)
            {
                labels.Add(option?.Label);
            }

            var chosen = await prompts.AskAsync(action.Prompt, labels, ct);

            if (chosen == null || chosen.Value < 0 || chosen.Value >= action.Options.Count)
            {
                return; // dismissed or invalid; nothing to run
            }

            var child = action.Options[chosen.Value]?.Child;
            if (child == null)
            {
                return;
            }

            if (child.IsBlocking)
            {
                await ExecuteActionAsync(child, context, ct);
            }
            else
            {
                _tracker.Start(ExecuteActionAsync(child, context, ct));
            }
        }

        private async Task ExecuteCutsceneAsync(CutsceneActionDefinition action, GameContext context, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(action.ResourceId))
            {
                context.Services.Log.Error("[TruthCardGame] CutsceneAction has no resource assigned.");
                return;
            }

            var cutscenes = context.Services.Cutscene;
            if (cutscenes == null)
            {
                context.Services.Log.Error("[TruthCardGame] CutsceneAction: no cutscene service available.");
                return;
            }

            await cutscenes.PlayAsync(action.ResourceId, ct);
        }
    }
}
