using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// One drawable card: tags for filtering plus the actions it triggers.
    /// Data-only — all behavior lives in its action assets.
    /// </summary>
    [CreateAssetMenu(fileName = "Card", menuName = "TruthCardGame/Card")]
    public sealed class Card : ScriptableObject
    {
        [Tooltip("Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.")]
        [SerializeField] private string id;
        [SerializeField] private string title = "Untitled Card";
        [Tooltip("Tags used by the executor's draw filters.")]
        [SerializeField] private List<string> tags = new List<string>();
        [Tooltip("Actions executed in order when this card is drawn.")]
        [SerializeField] private List<CardAction> actions = new List<CardAction>();

        public string Id => id;
        public string Title => title;
        public IReadOnlyList<string> Tags => tags;
        public IReadOnlyList<CardAction> Actions => actions;

        /// <summary>Mints the stable ID on first call; no-op once set. Called by OnValidate and authoring tooling.</summary>
        public void EnsureId()
        {
            if (string.IsNullOrEmpty(id)) id = System.Guid.NewGuid().ToString("N");
        }

        private void OnValidate()
        {
            EnsureId();
        }

        /// <summary>Converts to the portable definition, preserving order and null action entries.</summary>
        public TruthCardGame.Content.CardDefinition ToDefinition(CutsceneBindingRegistry registry)
        {
            var definition = new TruthCardGame.Content.CardDefinition { Id = id, Title = title };
            if (tags != null) definition.Tags.AddRange(tags);
            if (actions != null)
            {
                foreach (var action in actions)
                {
                    definition.Actions.Add(action == null ? null : action.ToDefinition(registry));
                }
            }
            return definition;
        }
    }
}
