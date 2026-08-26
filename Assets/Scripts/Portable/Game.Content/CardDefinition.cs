using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class CardDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>Executed in order. Entries may be null (skipped at execution, matching baseline).</summary>
        public List<GameActionDefinition> Actions { get; set; } = new List<GameActionDefinition>();
    }
}
