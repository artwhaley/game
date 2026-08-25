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
        [SerializeField] private string title = "Untitled Phase";
        [Tooltip("Cards drawn during this phase must have ALL of these tags.")]
        [SerializeField] private List<string> mustIncludeTags = new List<string>();
        [Tooltip("Cards drawn during this phase must have NONE of these tags.")]
        [SerializeField] private List<string> mustExcludeTags = new List<string>();
        [Tooltip("Unscaled draw count range: the driver picks one number per phase start.")]
        [SerializeField] private int minCards = 1;
        [SerializeField] private int maxCards = 3;

        public string Title => title;
        public IReadOnlyList<string> MustIncludeTags => mustIncludeTags;
        public IReadOnlyList<string> MustExcludeTags => mustExcludeTags;
        public int MinCards => minCards;
        public int MaxCards => maxCards;
    }
}