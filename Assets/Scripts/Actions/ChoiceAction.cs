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
            [Tooltip("Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.")]
            [SerializeField] private string id;
            [SerializeField] private string label = "Option";
            [SerializeField] private CardAction action;

            public string Id => id;
            public string Label => label;
            public CardAction Action => action;

            public void EnsureId()
            {
                if (string.IsNullOrEmpty(id)) id = System.Guid.NewGuid().ToString("N");
            }
        }

        [SerializeField, TextArea] private string prompt = "Choose:";
        [SerializeField] private List<ChoiceOption> options = new List<ChoiceOption>();

        public string Prompt => prompt;
        public IReadOnlyList<ChoiceOption> Options => options;

        /// <summary>Mints this action's ID and every option's ID (options are nested data, not assets, so the owning action owns their minting).</summary>
        public override void EnsureId()
        {
            base.EnsureId();
            if (options != null)
            {
                foreach (var option in options)
                {
                    option?.EnsureId();
                }
            }
        }

        // Base OnValidate is hidden by this declaration; both the action id and option ids are covered by EnsureId.
        private void OnValidate()
        {
            EnsureId();
        }

        public override TruthCardGame.Content.GameActionDefinition ToDefinition(CutsceneBindingRegistry registry)
        {
            var definition = new TruthCardGame.Content.ChoiceActionDefinition
            {
                Id = Id,
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
                    Id = option.Id,
                    Label = option.Label,
                    Child = option.Action == null ? null : option.Action.ToDefinition(registry)
                });
            }
            return definition;
        }
    }
}
