using System;

namespace TruthCardGame.Core
{
    /// <summary>
    /// What an action needs to know about the game at execution time: the
    /// player plus the host services. Extension seam for future needs.
    /// </summary>
    public sealed class GameContext
    {
        public Player Player { get; }
        public CoreServices Services { get; }

        public GameContext(Player player, CoreServices services)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }
    }
}
