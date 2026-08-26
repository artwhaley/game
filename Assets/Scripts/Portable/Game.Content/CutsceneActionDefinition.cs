namespace TruthCardGame.Content
{
    public sealed class CutsceneActionDefinition : GameActionDefinition
    {
        /// <summary>Host-resolved resource key. Null/empty means "missing timeline" (logged no-op, matching baseline).</summary>
        public string ResourceId { get; set; }
    }
}
