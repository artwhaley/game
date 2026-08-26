using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class CardDeckDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        /// <summary>Display name (authoring convenience; nullable in the schema).</summary>
        public string Title { get; set; }

        /// <summary>
        /// Ordered IDs of top-level Cards; the same Card ID may appear more
        /// than once. Dense lists: null/empty entries are skipped defensively,
        /// but normal content has none.
        /// </summary>
        public List<string> CardIds { get; set; } = new List<string>();
    }
}
