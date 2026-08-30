namespace TruthCardGame.Content
{
    /// <summary>
    /// The single explicit vocabulary of Resource kinds. Everything that names
    /// a Resource kind — the Core runtime kind validation, SQLite persistence,
    /// the WPF Resource browser, Unity host implementations — references these
    /// constants so the vocabulary cannot drift. Resource remains a portable
    /// identity (Id + Kind + Name); unknown future kinds must still load safely.
    /// </summary>
    public static class ResourceKinds
    {
        public const string Cutscene = "cutscene";
        public const string ToyPattern = "toy_pattern";
    }
}
