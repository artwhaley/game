namespace TruthCardGame.Content
{
    public sealed class ChoiceOptionDefinition
    {
        public string Label { get; set; } = "";

        /// <summary>May be null: a chosen option with no child action is a no-op (matching baseline).</summary>
        public GameActionDefinition Child { get; set; }
    }
}
