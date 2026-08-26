using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class SessionDefinition
    {
        public string Title { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public List<PhaseDefinition> Phases { get; set; } = new List<PhaseDefinition>();
    }
}
