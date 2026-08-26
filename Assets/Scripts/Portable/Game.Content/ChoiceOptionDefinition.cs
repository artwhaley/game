namespace TruthCardGame.Content
{
    public sealed class ChoiceOptionDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        public string Label { get; set; } = "";

        /// <summary>
        /// ID of the child Action, or null/empty for a legal no-op option
        /// (baseline "Cautious" semantics). Never embeds the Action.
        /// </summary>
        public string ChildActionId { get; set; }
    }
}
