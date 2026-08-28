using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// Authored Card classification tag. Distinct from Kinks, Equipment, and
    /// Smart Toy capabilities; distinct from any removed Phase tag concept.
    /// Phases query Cards by these tags (ALL/ANY, include-only; exclusion is
    /// deliberately deferred to a future status-effect system).
    /// </summary>
    public sealed class CardTagDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// Authored Kink. A Card carrying a Kink is eligible only when the user's
    /// preference for it is Love, Like, or Torture; DontConsent and
    /// Unconfigured (no profile row) both hard-exclude the Card.
    /// </summary>
    public sealed class KinkDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int SortOrder { get; set; }
    }

    /// <summary>Authored Equipment. A Card may require zero or more items; every listed item is required.</summary>
    public sealed class EquipmentDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>Simple text category for Milestone B grouping.</summary>
        public string Category { get; set; } = "";
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// Authored Smart Toy semantic capability (e.g. "vibrate", "rotate") —
    /// not a hardware model or protocol. A SessionType may require zero or
    /// more; a Card may require zero or more; all listed are required.
    /// </summary>
    public sealed class SmartToyCapabilityDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public int SortOrder { get; set; }
    }
}
