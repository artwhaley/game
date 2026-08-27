namespace TruthCardGame.Content
{
    /// <summary>
    /// One stable exported exit of a reusable Phase. Multiple PhaseGoto instances
    /// may reference the same exit ID; the enclosing Session PhaseReference projects
    /// exactly one Session-graph socket per exit. Identity is the ID — display names
    /// may be renamed freely and never affect wiring.
    /// </summary>
    public sealed class PhaseExitDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
