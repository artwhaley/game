using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// Authored Dialog Tag: a reusable classification chip for Dialog Snippets
    /// and a required-tag selector for Dialog From Tags actions. Stable ID is
    /// identity; the Title is author-facing and may repeat safely.
    /// Distinct from Card Tags (different catalog, different selection domain).
    /// </summary>
    public sealed class DialogTagDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// Authored reusable dialog line. Portable content text + tags — NOT a
    /// Resource, no Unity asset binding. `Name` is author-facing; `Text` is the
    /// displayed line. Tags reference DialogTagDefinition stable IDs.
    /// </summary>
    public sealed class DialogSnippetDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public int SortOrder { get; set; }
        public List<string> DialogTagIds { get; } = new List<string>();
    }
}
