using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class CardDeckDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        /// <summary>Entries may be null (ignored by matching, matching baseline).</summary>
        public List<CardDefinition> Cards { get; set; } = new List<CardDefinition>();
    }
}
