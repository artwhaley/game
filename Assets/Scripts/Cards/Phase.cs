using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// One stage of a Session. Defines which cards get drawn (tag filter) and
    /// how long the stage lasts (a random draw count within min/max, scaled by
    /// the session's live length modifier). Pure data — the SessionDriver walks
    /// phases; a phase never executes anything itself.
    /// </summary>
    [CreateAssetMenu(fileName = "Phase", menuName = "TruthCardGame/Phase")]
    public sealed class Phase : ScriptableObject
    {
        [Tooltip("Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.")]
        [SerializeField] private string id;
        [SerializeField] private string title = "Untitled Phase";
        [Tooltip("Cards drawn during this phase must have ALL of these tags.")]
        [SerializeField] private List<string> mustIncludeTags = new List<string>();
        [Tooltip("Cards drawn during this phase must have NONE of these tags.")]
        [SerializeField] private List<string> mustExcludeTags = new List<string>();
        [Tooltip("Unscaled draw count range: the driver picks one number per phase start.")]
        [SerializeField] private int minCards = 1;
        [SerializeField] private int maxCards = 3;

        public string Id => id;
        public string Title => title;
        public IReadOnlyList<string> MustIncludeTags => mustIncludeTags;
        public IReadOnlyList<string> MustExcludeTags => mustExcludeTags;
        public int MinCards => minCards;
        public int MaxCards => maxCards;

        /// <summary>Mints the stable ID on first call; no-op once set. Called by OnValidate and authoring tooling.</summary>
        public void EnsureId()
        {
            if (string.IsNullOrEmpty(id)) id = System.Guid.NewGuid().ToString("N");
        }

        private void OnValidate()
        {
            EnsureId();
        }

        public TruthCardGame.Content.PhaseDefinition ToDefinition()
        {
            var definition = new TruthCardGame.Content.PhaseDefinition
            {
                Id = id,
                Title = title,
                MinCards = minCards,
                MaxCards = maxCards
            };
            if (mustIncludeTags != null) definition.MustIncludeTags.AddRange(mustIncludeTags);
            if (mustExcludeTags != null) definition.MustExcludeTags.AddRange(mustExcludeTags);
            return definition;
        }
    }
}