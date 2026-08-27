using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Presents the player with a choice and executes the chosen option's
    /// nested action. Data shell; execution lives in Game.Core.
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

        public override TruthCardGame.Content.ActionInstanceDefinition ToDefinition(UnityContentGraphBuilder builder)
        {
            var definition = new TruthCardGame.Content.PromptChoiceInstanceDefinition
            {
                Id = Id,
                IsBlocking = IsBlocking,
                Prompt = prompt
            };
            if (options == null) return definition;

            foreach (var option in options)
            {
                if (option == null) continue;

                // Each option owns its own nested sequence; the child action
                // becomes one instance inside it (instances are never shared).
                var optionDefinition = new TruthCardGame.Content.PromptChoiceOptionDefinition
                {
                    Id = option.Id,
                    Label = option.Label,
                    Sequence = new TruthCardGame.Content.ActionSequenceDefinition { Id = "seq-" + option.Id }
                };
                if (option.Action != null)
                {
                    optionDefinition.Sequence.Instances.Add(option.Action.ToDefinition(builder));
                }
                definition.Options.Add(optionDefinition);
            }
            return definition;
        }
    }
}
