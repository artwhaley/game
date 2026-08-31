namespace TruthCardGame.Content
{
    /// <summary>One persisted folder in the Cards library hierarchy.</summary>
    public sealed class CardFolderDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ParentId { get; set; }
        public string Path { get; set; } = "";
        public int SortOrder { get; set; }
    }
}
