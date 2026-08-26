using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Executes card action references with baseline semantics:
    /// - null/empty action references are skipped;
    /// - blocking actions are awaited in order;
    /// - nonblocking actions are started through the background tracker and
    ///   execution continues immediately;
    /// - Choice children resolve by ChildActionId through the catalog;
    /// - reference cycles (Choice -> child -> ... -> itself) are stopped with
    ///   a clear logged failure instead of unbounded recursion.
    /// Misconfiguration (missing prompt/cutscene service, empty options,
    /// dismissed choice, missing resource) logs an error and no-ops, exactly
    /// as before. A non-empty Action ID missing from the catalog fails loudly
    /// with context. Unexpected exceptions fault the task and must be observed
    /// by the caller or the tracker.
    /// </summary>
    public sealed class ActionExecutor
    {
        private readonly ContentCatalog _catalog;
        private readonly BackgroundActionTracker _tracker;

        public ActionExecutor(ContentCatalog catalog, BackgroundActionTracker tracker)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }

        /// <summary>Runs one card's actions in order.</summary>
        public async Task ExecuteCardAsync(CardDefinition card, GameContext context, CancellationToken cancellationToken)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            var actionIds = card.ActionIds;
            if (actionIds == null) return;

            for (var i = 0; i < actionIds.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = actionIds[i];
                if (string.IsNullOrEmpty(id)) continue;

                var action = _catalog.ActionById(id);

                if (action.IsBlocking)
                {
                    await ExecuteActionAsync(action, context, new HashSet<string>(), cancellationToken);
                }
                else
                {
                    _tracker.Start(ExecuteActionAsync(action, context, new HashSet<string>(), cancellationToken));
                }
            }
        }

        /// <summary>Runs a single action to completion (used for blocking actions and chosen children).</summary>
        public Task ExecuteActionAsync(GameActionDefinition action, GameContext context, CancellationToken cancellationToken)
        {
            return ExecuteActionAsync(action, context, new HashSet<string>(), cancellationToken);
        }

        private async Task ExecuteActionAsync(GameActionDefinition action, GameContext context, HashSet<string> chain, CancellationToken cancellationToken)
        {
            var id = action.Id;
            if (!string.IsNullOrEmpty(id) && !chain.Add(id))
            {
                context.Services.Log.Error($"[TruthCardGame] Action reference cycle detected at action '{id}'; stopping this chain.");
                return;
            }

            try
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
                        await ExecuteChoiceAsync(choice, context, chain, cancellationToken);
                        break;
                    case CutsceneActionDefinition cutscene:
                        await ExecuteCutsceneAsync(cutscene, context, cancellationToken);
                        break;
                    default:
                        context.Services.Log.Error($"[TruthCardGame] No executor for action type {action.GetType().Name}.");
                        break;
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(id)) chain.Remove(id);
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

        private async Task ExecuteChoiceAsync(ChoiceActionDefinition action, GameContext context, HashSet<string> chain, CancellationToken ct)
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

            var childId = action.Options[chosen.Value]?.ChildActionId;
            if (string.IsNullOrEmpty(childId))
            {
                return;
            }

            var child = _catalog.ActionById(childId);

            if (child.IsBlocking)
            {
                await ExecuteActionAsync(child, context, chain, ct);
            }
            else
            {
                _tracker.Start(ExecuteActionAsync(child, context, new HashSet<string>(), ct));
            }
        }

        private async Task ExecuteCutsceneAsync(CutsceneActionDefinition action, GameContext context, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(action.ResourceId))
            {
                context.Services.Log.Error("[TruthCardGame] CutsceneAction has no resource assigned.");
                return;
            }

            // The portable Resource must exist in the snapshot; hosts resolve
            // their own bindings off this ID afterwards.
            _catalog.ResourceById(action.ResourceId);

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
