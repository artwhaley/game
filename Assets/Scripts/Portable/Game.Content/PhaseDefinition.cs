using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class PhaseDefinition
    {
        public string Title { get; set; } = "";
        public List<string> MustIncludeTags { get; set; } = new List<string>();
        public List<string> MustExcludeTags { get; set; } = new List<string>();
        public int MinCards { get; set; } = 1;
        public int MaxCards { get; set; } = 3;
    }
}
