using System;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Walks a session's phases: owns the current phase, the unscaled draw
    /// target picked once per phase, and the live length-modifier math.
    ///
    /// Advance rule (evaluated live on every card completion):
    ///   cardsDrawn >= Round(baseTarget * lengthModifier()), clamped >= 1
    /// Nothing is baked at session start, so a mid-session modifier change
    /// takes effect on the very next draw.
    ///
    /// A phase with no matching cards advances early with a warning. The
    /// final phase's completion marks the session complete.
    /// </summary>
    public sealed class SessionDriver
    {
        private readonly SessionDefinition _session;
        private readonly Func<float> _lengthModifier;
        private readonly IGameLog _log;
        private readonly IRandomSource _rng;

        private int _phaseIndex;
        private int _baseTarget;
        private int _cardsDrawn;

        public bool IsComplete { get; private set; }

        public int PhaseIndex => IsComplete ? -1 : _phaseIndex;

        public string[] CurrentMustInclude { get; private set; }
        public string[] CurrentMustExclude { get; private set; }

        public SessionDriver(
            SessionDefinition session,
            Func<float> lengthModifier = null,
            IGameLog log = null,
            IRandomSource phaseRng = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _lengthModifier = lengthModifier ?? (() => 1f);
            _log = log ?? new NullGameLog();
            _rng = phaseRng ?? new SystemRandomSource();
            _phaseIndex = 0;
            _baseTarget = NextBaseTarget(_rng);
            CurrentMustInclude = ToArray(CurrentPhase.MustIncludeTags);
            CurrentMustExclude = ToArray(CurrentPhase.MustExcludeTags);
        }

        private PhaseDefinition CurrentPhase
        {
            get
            {
                if (_phaseIndex < 0 || _phaseIndex >= _session.Phases.Count)
                {
                    throw new InvalidOperationException("SessionDriver: no current phase.");
                }
                return _session.Phases[_phaseIndex];
            }
        }

        private int NextBaseTarget(IRandomSource rng)
        {
            var min = Math.Max(1, CurrentPhase.MinCards);
            var max = Math.Max(min, CurrentPhase.MaxCards);
            return rng.NextInt(min, max + 1);
        }

        private static string[] ToArray(System.Collections.Generic.IReadOnlyList<string> list)
        {
            if (list == null || list.Count == 0) return Array.Empty<string>();
            var result = new string[list.Count];
            for (var i = 0; i < list.Count; i++) result[i] = list[i];
            return result;
        }

        public int CurrentTarget()
        {
            var scaled = (int)MathF.Round(_baseTarget * _lengthModifier(), MidpointRounding.ToEven);
            return Math.Max(1, scaled);
        }

        public int Remaining()
        {
            return IsComplete ? 0 : Math.Max(0, CurrentTarget() - _cardsDrawn);
        }

        public void OnCardCompleted()
        {
            if (IsComplete) return;

            _cardsDrawn++;

            if (_cardsDrawn < CurrentTarget())
            {
                return;
            }

            AdvancePhase();
        }

        public void OnNoMatchingCard()
        {
            if (IsComplete) return;

            _log.Warning($"[TruthCardGame] Session '{_session.Title}' phase '{CurrentPhase.Title}' drew no matching cards; advancing early.");
            AdvancePhase();
        }

        private void AdvancePhase()
        {
            _cardsDrawn = 0;
            _phaseIndex++;

            if (_phaseIndex >= _session.Phases.Count)
            {
                IsComplete = true;
                CurrentMustInclude = Array.Empty<string>();
                CurrentMustExclude = Array.Empty<string>();
                return;
            }

            _baseTarget = NextBaseTarget(_rng);
            CurrentMustInclude = ToArray(CurrentPhase.MustIncludeTags);
            CurrentMustExclude = ToArray(CurrentPhase.MustExcludeTags);
        }
    }
}
