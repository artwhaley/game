namespace TruthCardGame.Content
{
    /// <summary>
    /// A simple authored category for Sessions (e.g. JOI, Stroker Toy, Butt Stuff).
    /// A Session references exactly one SessionType. A future launcher will filter
    /// eligible Sessions by profile/device requirements and select one of the chosen
    /// type; this stack establishes only the data entity plus a deterministic/random
    /// selection seam (Game.Core, Ticket 06).
    /// </summary>
    public sealed class SessionTypeDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }
}
