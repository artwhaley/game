using System;
using System.Collections.Generic;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Pure, clock-testable toy output state machine implementing the frozen
    /// Toy command semantics (Docs/ToyPatternDialog/01-contract.md).
    ///
    /// - Output state is keyed by Capability ID.
    /// - A new command for a capability supersedes the previous command for it.
    /// - Commands for different capabilities are independent and coexist.
    /// - A Timed command owns a per-capability generation: when its timer later
    ///   expires it clears the capability ONLY if it still owns the current
    ///   generation, so a superseded timed command can never stop a newer one
    ///   (stale-timer rule).
    /// - SetPattern is a persistent, non-background command (no expiry; lives
    ///   across Card/Phase transitions until superseded or teardown).
    /// - StopAll is lifecycle teardown that clears every capability.
    ///
    /// The clock is injected as a function returning a monotonic tick count
    /// (ms); call <see cref="Elapse"/> to apply natural expiries. This class is
    /// deliberately free of Tasks/Timers so both the WPF host and the
    /// deterministic regression suite can drive it.
    /// </summary>
    public sealed class ToyOutputSimulator
    {
        private enum CommandKind { None, Timed, Persistent }

        private sealed class Command
        {
            public CommandKind Kind;
            public string PatternId;
            public long Generation;
            public long ExpiryTicks; // Timed only
        }

        private readonly Func<long> _ticks;
        private readonly Dictionary<string, Command> _byCapability = new Dictionary<string, Command>();
        private long _nextGeneration;

        public ToyOutputSimulator(Func<long> ticks)
        {
            _ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
        }

        /// <summary>Current clock value (ms), the reference for expiries.</summary>
        public long NowTicks => _ticks();

        /// <summary>
        /// Applies a Timed Toy Pattern command. Supersedes any prior command on
        /// the capability and returns the new command's generation token.
        /// </summary>
        public long PlayFor(string capabilityId, string patternResourceId, TimeSpan duration)
        {
            if (string.IsNullOrEmpty(capabilityId)) throw new ArgumentNullException(nameof(capabilityId));
            if (string.IsNullOrEmpty(patternResourceId)) throw new ArgumentNullException(nameof(patternResourceId));
            if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

            Elapse();
            var cmd = new Command
            {
                Kind = CommandKind.Timed,
                PatternId = patternResourceId,
                Generation = ++_nextGeneration,
                ExpiryTicks = _ticks() + (long)duration.TotalMilliseconds
            };
            _byCapability[capabilityId] = cmd;
            return cmd.Generation;
        }

        /// <summary>
        /// Applies a persistent Set Toy Pattern command (no expiry). Supersedes
        /// any prior command on the capability.
        /// </summary>
        public long SetPattern(string capabilityId, string patternResourceId)
        {
            if (string.IsNullOrEmpty(capabilityId)) throw new ArgumentNullException(nameof(capabilityId));
            if (string.IsNullOrEmpty(patternResourceId)) throw new ArgumentNullException(nameof(patternResourceId));

            Elapse();
            var cmd = new Command
            {
                Kind = CommandKind.Persistent,
                PatternId = patternResourceId,
                Generation = ++_nextGeneration
            };
            _byCapability[capabilityId] = cmd;
            return cmd.Generation;
        }

        /// <summary>
        /// Applies natural expiries due at the current clock value. A Timed
        /// command is cleared only while it still owns the capability's current
        /// generation (stale timers from superseded commands are no-ops).
        /// </summary>
        public void Elapse()
        {
            var now = _ticks();
            var toClear = new List<string>();
            foreach (var kvp in _byCapability)
            {
                var cmd = kvp.Value;
                if (cmd.Kind == CommandKind.Timed && cmd.ExpiryTicks <= now)
                    toClear.Add(kvp.Key);
            }
            foreach (var cap in toClear)
            {
                var cmd = _byCapability[cap];
                if (cmd.Kind == CommandKind.Timed && cmd.ExpiryTicks <= now)
                {
                    cmd.Kind = CommandKind.None;
                    cmd.PatternId = null;
                    cmd.Generation = 0;
                }
            }
        }

        /// <summary>Clears every capability (lifecycle teardown).</summary>
        public void StopAll()
        {
            _byCapability.Clear();
        }

        public void RemoveCapability(string capabilityId)
        {
            if (capabilityId != null) _byCapability.Remove(capabilityId);
        }

        public bool HasCapability(string capabilityId)
        {
            return capabilityId != null && _byCapability.TryGetValue(capabilityId, out var cmd) && cmd.Kind != CommandKind.None;
        }

        /// <summary>
        /// Whether the given command generation is still the live owner of the
        /// capability (used by hosts to know if a PlayFor task still owns the
        /// slot, i.e. supersession detection).
        /// </summary>
        public bool OwnsGeneration(string capabilityId, long generation)
        {
            return capabilityId != null
                && _byCapability.TryGetValue(capabilityId, out var cmd)
                && cmd.Kind != CommandKind.None
                && cmd.Generation == generation;
        }

        /// <summary>Pattern currently active on the capability, or null if idle.</summary>
        public string ActivePattern(string capabilityId)
        {
            if (capabilityId == null) return null;
            Elapse();
            return _byCapability.TryGetValue(capabilityId, out var cmd)
                && cmd.Kind != CommandKind.None
                    ? cmd.PatternId
                    : null;
        }

        /// <summary>Whether the capability has a currently-active Timed command.</summary>
        public bool IsTimedActive(string capabilityId)
        {
            if (capabilityId == null) return false;
            Elapse();
            return _byCapability.TryGetValue(capabilityId, out var cmd) && cmd.Kind == CommandKind.Timed;
        }

        /// <summary>
        /// Read-only snapshot of every capability's live output state. Used by
        /// hosts (WPF simulator UI) and by the regression suite to assert state
        /// after time advances.
        /// </summary>
        public IReadOnlyCollection<ToyCapabilityState> Snapshot()
        {
            Elapse();
            var list = new List<ToyCapabilityState>();
            foreach (var kvp in _byCapability)
            {
                var cmd = kvp.Value;
                if (cmd.Kind == CommandKind.None) continue;
                list.Add(new ToyCapabilityState(
                    kvp.Key, cmd.Kind == CommandKind.Timed, cmd.PatternId,
                    cmd.Kind == CommandKind.Timed ? Math.Max(0, cmd.ExpiryTicks - _ticks()) : 0L));
            }
            return list;
        }
    }

    /// <summary>Immutable view of one capability's live toy output.</summary>
    public sealed class ToyCapabilityState
    {
        public string CapabilityId { get; }
        public bool IsTimed { get; }
        public string PatternResourceId { get; }
        public long RemainingMs { get; }

        public ToyCapabilityState(string capabilityId, bool isTimed, string patternResourceId, long remainingMs)
        {
            CapabilityId = capabilityId;
            IsTimed = isTimed;
            PatternResourceId = patternResourceId;
            RemainingMs = remainingMs;
        }
    }
}