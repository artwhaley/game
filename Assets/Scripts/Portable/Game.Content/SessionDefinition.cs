using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class SessionDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>Ordered session-owned slots; each slot's candidates reference reusable Phases.</summary>
        public List<PhaseSlotDefinition> PhaseSlots { get; set; } = new List<PhaseSlotDefinition>();
    }
}
