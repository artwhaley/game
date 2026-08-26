using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Presents the player with a choice and branches to one child action.
    /// Data shell; execution lives in Game.Core.
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
