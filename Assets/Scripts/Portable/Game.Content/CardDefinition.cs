using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class CardDefinition
    {
        public string Title { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>Executed in order. Entries may be null (skipped at execution, matching baseline).</summary>
        public List<GameActionDefinition> Actions { get; set; } = new List<GameActionDefinition>();
    }
}
