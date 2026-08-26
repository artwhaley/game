using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class CardDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Executed in order. Entries are IDs of top-level Actions; the same
        /// Action ID may appear more than once. Dense lists: null/empty
        /// entries are skipped defensively, but normal content has none.
        /// </summary>
        public List<string> ActionIds { get; set; } = new List<string>();
    }
}
