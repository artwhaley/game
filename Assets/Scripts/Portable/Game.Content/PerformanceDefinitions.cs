using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// Authored semantic Performance Tag (for example "playful", "tease",
    /// "stern", "comforting"). A separate namespace from Card Tags and Dialog
    /// Tags; owned by WPF/SQLite and referenced by Unity ingredient membership
    /// through stable IDs. Retiring a tag preserves identity and resolution so
    /// existing content keeps working, while new authoring hides it.
    /// </summary>
    public sealed class PerformanceTagDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int SortOrder { get; set; }

        /// <summary>Retired tags keep identity and remain resolvable; they are hidden from new selections.</summary>
        public bool IsRetired { get; set; }
    }

    /// <summary>
    /// How a Performance Event chooses its destination anchor. Fixtures such as
    /// room names are anchors, never Core enums.
    /// </summary>
    public enum PerformanceStagingPolicy
    {
        /// <summary>Default: keep the current anchor, permitting a compatible posture change.</summary>
        Stay = 0,

        /// <summary>Pick any reachable anchor where a compatible acting combination exists.</summary>
        ChooseCompatible = 1,

        /// <summary>Require an anchor in a different location group than the current one.</summary>
        DifferentLocation = 2,

        /// <summary>Require the named anchor (NamedAnchorId).</summary>
        NamedLocation = 3,
    }

    /// <summary>
    /// A reusable Conversation Performance Event: a small semantic request the
    /// Perform action asks Core to satisfy. It carries an ALL/ANY Performance
    /// Tag query, a staging policy, optional anchor/posture restrictions and the
    /// automatic refresh-at-dialogue-start flag. No layered query tables, rest
    /// probabilities, driver kind, cadence or persistent mood.
    /// </summary>
    public sealed class ConversationPerformanceEventDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>Authoring order; runtime selection ignores it.</summary>
        public int SortOrder { get; set; }

        /// <summary>All required tags must be present (ALL) versus at least one (ANY).</summary>
        public bool RequireAllTags { get; set; } = true;

        /// <summary>Semantic Performance Tag IDs the expressive ingredients must satisfy.</summary>
        public List<string> PerformanceTagIds { get; } = new List<string>();

        public PerformanceStagingPolicy StagingPolicy { get; set; } = PerformanceStagingPolicy.Stay;

        /// <summary>Destination anchor when StagingPolicy is NamedLocation; ignored otherwise.</summary>
        public string NamedAnchorId { get; set; } = "";

        /// <summary>Optional destination anchor restriction; empty means no extra restriction.</summary>
        public List<string> AllowedAnchorIds { get; } = new List<string>();

        /// <summary>Optional posture restriction; empty means no extra restriction.</summary>
        public List<string> AllowedPostureIds { get; } = new List<string>();

        /// <summary>Automatic compatible acting refresh immediately before blocking dialogue. On by default.</summary>
        public bool RefreshAtDialogueStart { get; set; } = true;
    }

    /// <summary>
    /// Perform(eventId): a blocking ordinary activity with no authored blocking
    /// toggle or action-local overrides. It waits for host readiness, then
    /// returns while Unity retains accepted presentation state. It is not an
    /// endless task and carries no mood, cadence or reroll parameters.
    /// </summary>
    public sealed class PerformInstanceDefinition : ActionInstanceDefinition
    {
        public string EventId { get; set; } = "";
    }
}
