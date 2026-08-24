namespace TruthCardGame
{
    /// <summary>
    /// What an action needs to know about the game at execution time.
    /// The extension seam for future needs (e.g. a bluetooth toy reference,
    /// an RNG, multiplayer state) — actions never reach into the scene.
    /// </summary>
    public sealed class GameContext
    {
        public Player Player { get; }

        public GameContext(Player player)
        {
            Player = player;
        }
    }
}
