using System;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Walks a session's phases: owns the current phase, the unscaled draw
    /// target picked once per phase, and the live length-modifier math. The
    /// CardExecutor stays "draw one matching card"; the driver owns which
    /// filter to use and when to advance.
    ///
    /// Advance rule (evaluated live on every card completion):
    ///   cardsDrawn >= Round(baseTarget * SessionConfig.LengthModifier), clamped ≥ 1
    /// Nothing is baked at session start, so a mid-session modifier change
    /// (e.g. a future choice action) takes effect on the very next draw —
    /// growing extends the phase, shrinking can end it immediately.
    ///
    /// A phase with no matching cards advances early with a warning (expected
    /// never to happen with authored content). The final phase's completion
    /// marks the session complete; the game layer returns to the menu.
    /// </summary>
    public sealed class SessionDriver
    {
        private readonly Session _session;
        private readonly Func<float> _lengthModifier;
        private readonly Action<string> _warn;
        private readonly System.Random _rng;

        private int _phaseIndex;
        private int _baseTarget;   // unscaled draw target for the current phase
        private int _cardsDrawn;

        /// <summary>True once the last phase has run its draws.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>Phase index currently being drawn from; -1 once complete.</summary>
        public int PhaseIndex => IsComplete ? -1 : _phaseIndex;

        /// <summary>Tags that filter draws for the current phase.</summary>
        public string[] CurrentMustInclude { get; private set; }
        public string[] CurrentMustExclude { get; private set; }

        public SessionDriver(
            Session session,
            Func<float> lengthModifier = null,
            Action<string> warn = null,
            System.Random rng = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _lengthModifier = lengthModifier ?? (() => SessionConfig.LengthModifier);
            _warn = warn ?? Debug.LogWarning;
            _rng = rng ?? new System.Random();
            _phaseIndex = 0;
            _baseTarget = NextBaseTarget(_rng);
            CurrentMustInclude = ToArray(CurrentPhase.MustIncludeTags);
            CurrentMustExclude = ToArray(CurrentPhase.MustExcludeTags);
        }

        private Phase CurrentPhase
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

        private int NextBaseTarget(System.Random rng)
        {
            var min = Mathf.Max(1, CurrentPhase.MinCards);
            var max = Mathf.Max(min, CurrentPhase.MaxCards);
            return rng.Next(min, max + 1);
        }

        private static string[] ToArray(System.Collections.Generic.IReadOnlyList<string> list)
        {
            if (list == null || list.Count == 0) return Array.Empty<string>();
            var result = new string[list.Count];
            for (var i = 0; i < list.Count; i++) result[i] = list[i];
            return result;
        }

        /// <summary>
        /// The scaled draw target for the current phase, clamped ≥ 1. Read live
        /// so modifier changes rebake immediately.
        /// </summary>
        public int CurrentTarget()
        {
            var scaled = Mathf.RoundToInt(_baseTarget * _lengthModifier());
            return Mathf.Max(1, scaled);
        }

        /// <summary>How many draws remain in the current phase (live).</summary>
        public int Remaining()
        {
            return IsComplete ? 0 : Mathf.Max(0, CurrentTarget() - _cardsDrawn);
        }

        /// <summary>Called after each card finishes executing.</summary>
        public void OnCardCompleted()
        {
            if (IsComplete) return;

            _cardsDrawn++;

            if (_cardsDrawn < CurrentTarget())
            {
                return; // phase continues
            }

            AdvancePhase();
        }

        /// <summary>Called when the executor found no card matching the phase's filter.</summary>
        public void OnNoMatchingCard()
        {
            if (IsComplete) return;

            _warn($"[TruthCardGame] Session '{_session.Title}' phase '{CurrentPhase.Title}' drew no matching cards; advancing early.");
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