using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class SessionDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public List<PhaseDefinition> Phases { get; set; } = new List<PhaseDefinition>();
    }
}
