using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// A blocking card action that presents the player with a choice (via
    /// context.Services.Prompts) and then runs the chosen child action,
    /// honoring that child's own blocking flag. Branching lives here, inside
    /// the action, so the executor never needs to know about choices.
    ///
    /// Card-level choices (choose which card comes next) are a different,
    /// future mechanism that branches in the session driver — not this action.
    /// </summary>
    [CreateAssetMenu(fileName = "ChoiceAction", menuName = "TruthCardGame/Actions/Choice")]
    public sealed class ChoiceAction : CardAction
    {
        [Serializable]
        public sealed class ChoiceOption
        {
            [SerializeField] private string label = "Option";
            [SerializeField] private CardAction action;

            public string Label => label;
            public CardAction Action => action;
        }

        [SerializeField, TextArea] private string prompt = "Choose:";
        [SerializeField] private List<ChoiceOption> options = new List<ChoiceOption>();

        public string Prompt => prompt;
        public IReadOnlyList<ChoiceOption> Options => options;

        public override IEnumerator Execute(GameContext context)
        {
            var prompts = context.Services?.Prompts;
            if (prompts == null)
            {
                Debug.LogError("[TruthCardGame] ChoiceAction: no IPromptService in context. Is the Game scene wired (rebuild with Build Scenes)?");
                yield break;
            }
            if (options == null || options.Count == 0)
            {
                Debug.LogError("[TruthCardGame] ChoiceAction: no options configured. Add options on the action asset.");
                yield break;
            }

            var chosen = -1;
            yield return prompts.Ask(prompt, options.Select(o => o.Label).ToList(), i => chosen = i);

            if (chosen < 0 || chosen >= options.Count)
            {
                yield break; // dialog dismissed without a choice; nothing to run
            }

            var child = options[chosen].Action;
            if (child == null)
            {
                yield break;
            }

            if (child.IsBlocking)
            {
                yield return child.Execute(context);
            }
            else if (context.Services.Runner != null)
            {
                context.Services.Runner.StartRoutine(child.Execute(context));
            }
            else
            {
                Debug.LogError("[TruthCardGame] ChoiceAction: chosen continuous child but no coroutine runner in context.");
            }
        }

        public override TruthCardGame.Content.GameActionDefinition ToDefinition(CutsceneBindingRegistry registry)
        {
            var definition = new TruthCardGame.Content.ChoiceActionDefinition
            {
                IsBlocking = IsBlocking,
                Prompt = prompt
            };
            if (options == null) return definition;

            foreach (var option in options)
            {
                if (option == null)
                {
                    definition.Options.Add(null);
                    continue;
                }
                definition.Options.Add(new TruthCardGame.Content.ChoiceOptionDefinition
                {
                    Label = option.Label,
                    Child = option.Action == null ? null : option.Action.ToDefinition(registry)
                });
            }
            return definition;
        }
    }
}