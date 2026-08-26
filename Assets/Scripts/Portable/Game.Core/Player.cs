using System.Collections.Generic;

namespace TruthCardGame.Core
{
    /// <summary>One participant in a game session. Single-player for now.</summary>
    public sealed class Player
    {
        public string Name { get; }
        public PlayerStats Stats { get; }

        public Player(string name)
        {
            Name = name;
            Stats = new PlayerStats();
        }
    }
}
