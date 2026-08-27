using System;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Provides the fresh Card RNG for each new PhaseRun.\n    ///\n    /// Deterministic: a seeded factory yields the same per-run sequences across\n    /// runs, so graph VM tests are reproducible. Each call creates a NEW,\n    /// independent RNG object — a suspended PhaseRun keeps its own object and\n    /// state untouched while other runs execute (no serializable RNG snapshots\n    /// needed for the in-process continuation stack).\n    /// </summary>
    public sealed class PhaseRunRngFactory
    {
        private readonly int _baseSeed;
        private int _next;

        public PhaseRunRngFactory()
        {
            _baseSeed = Environment.TickCount;
        }

        public PhaseRunRngFactory(int baseSeed)
        {
            _baseSeed = baseSeed;
        }

        /// <summary>Returns a distinct RNG instance for one fresh PhaseRun. Never shared between runs.</summary>
        public IRandomSource Create()
        {
            // Derive a fresh seed per call; SystemRandomSource itself is cheap
            // and stateless to hand out.
            return new SystemRandomSource(_baseSeed + _next++);
        }
    }
}
