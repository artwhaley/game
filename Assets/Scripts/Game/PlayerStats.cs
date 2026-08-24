using System.Collections.Generic;

namespace TruthCardGame
{
    /// <summary>
    /// String-keyed stat counters (e.g. "courage"). No fixed schema — keys are
    /// whatever actions choose to use. Unknown keys read as 0.
    /// </summary>
    public sealed class PlayerStats
    {
        private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

        public int Get(string statKey)
        {
            return _values.TryGetValue(statKey, out var value) ? value : 0;
        }

        public void Add(string statKey, int amount)
        {
            _values.TryGetValue(statKey, out var value);
            _values[statKey] = value + amount;
        }
    }
}
