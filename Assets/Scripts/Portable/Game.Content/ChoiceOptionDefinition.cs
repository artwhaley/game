namespace TruthCardGame.Content
{
    public sealed class ChoiceOptionDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        public string Label { get; set; } = "";

        /// <summary>May be null: a chosen option with no child action is a no-op (matching baseline).</summary>
        public GameActionDefinition Child { get; set; }
    }
}
