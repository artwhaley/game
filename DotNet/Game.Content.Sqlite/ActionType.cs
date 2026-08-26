namespace TruthCardGame.Content.Sqlite
{
    /// <summary>Values of action.action_type (the established discriminator names).</summary>
    public static class ActionType
    {
        public const string Debug = "debug";
        public const string StatIncrease = "statIncrease";
        public const string Choice = "choice";
        public const string Cutscene = "cutscene";
    }
}
