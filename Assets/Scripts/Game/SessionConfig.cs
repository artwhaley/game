namespace TruthCardGame
{
    /// <summary>
    /// State handed from the setup screen to the game screen across a scene
    /// load. Deliberately simple (static, not persisted) — this is a dev
    /// environment and sessions don't need saving yet.
    /// </summary>
    public static class SessionConfig
    {
        public static Session SelectedSession { get; set; }
    }
}
