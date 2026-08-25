namespace TruthCardGame
{
    /// <summary>
    /// State handed from the setup screen to the game screen across a scene
    /// load, plus the player's session-length preference from settings.
    /// Deliberately simple (static, not persisted) — this is a dev environment
    /// and sessions don't need saving yet.
    ///
    /// LengthModifier scales every phase's draw target live (the driver reads
    /// it on each card completion), so a mid-session change — e.g. a future
    /// choice action that lengthens or shortens the session — takes effect on
    /// the very next draw. Nothing is baked at session start.
    /// </summary>
    public static class SessionConfig
    {
        public static Session SelectedSession { get; set; }

        /// <summary>Multiplier on each phase's draw count. 1 = normal, 2 = twice as long.</summary>
        public static float LengthModifier { get; set; } = 1f;
    }
}