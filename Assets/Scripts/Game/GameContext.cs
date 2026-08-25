namespace TruthCardGame
{
    /// <summary>
    /// What an action needs to know about the game at execution time.
    /// Carries the current player plus the scene-side services (cutscene
    /// player, prompt service, coroutine runner) so actions never reach into
    /// the scene. Extension seam for future needs (e.g. a bluetooth toy
    /// reference, an RNG, multiplayer state).
    /// </summary>
    public sealed class GameContext
    {
        public Player Player { get; }
        public GameServices Services { get; }

        public GameContext(Player player, GameServices services = null)
        {
            Player = player;
            Services = services ?? new GameServices();
        }
    }
}