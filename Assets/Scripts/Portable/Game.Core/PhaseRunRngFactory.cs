using System;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Provides the fresh Card RNG for each new PhaseRun.\n    ///\n    /// Deterministic: a seeded factory yields the same per-run sequences across
    /// runs, so graph VM tests are reproducible. Each call creates a NEW,
    /// independent RNG object — a suspended PhaseRun keeps its own object and
    /// state untouched while other runs execute (no serializable RNG snapshots
    /// needed for the in-process continuation stack).
    ///
    /// A custom provider (tests) may hand out fully scripted RNGs instead of
    /// seeded system RNGs for exact card-order control.
    /// </summary>
    public sealed class PhaseRunRngFactory
    {
        private readonly int _baseSeed;
        private readonly Func<IRandomSource> _provider;
        private int _next;

        public PhaseRunRngFactory()
        {
            _baseSeed = Environment.TickCount;
        }

        public PhaseRunRngFactory(int baseSeed)
        {
            _baseSeed = baseSeed;
        }

        /// <summary>Custom RNG provider for exact, deterministic card-order tests.</summary>
        public PhaseRunRngFactory(Func<IRandomSource> provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>Returns a distinct RNG instance for one fresh PhaseRun. Never shared between runs.</summary>
        public IRandomSource Create()
        {
            if (_provider != null) return _provider();
            // Derive a fresh seed per call; SystemRandomSource itself is cheap
            // and stateless to hand out.
            return new SystemRandomSource(_baseSeed + _next++);
        }
    }
}
