using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class CardDeckDefinition
    {
        /// <summary>Entries may be null (ignored by matching, matching baseline).</summary>
        public List<CardDefinition> Cards { get; set; } = new List<CardDefinition>();
    }
}
