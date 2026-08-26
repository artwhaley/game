namespace TruthCardGame.Content
{
    public sealed class DebugActionDefinition : GameActionDefinition
    {
        public string Message { get; set; } = "";
        public float DelaySeconds { get; set; }
    }
}
