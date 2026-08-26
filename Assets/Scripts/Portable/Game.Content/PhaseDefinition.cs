using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class PhaseDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";
        public List<string> MustIncludeTags { get; set; } = new List<string>();
        public List<string> MustExcludeTags { get; set; } = new List<string>();
        public int MinCards { get; set; } = 1;
        public int MaxCards { get; set; } = 3;
    }
}
