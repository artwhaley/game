using System;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Walks a session's ordered PhaseSlots: owns the current slot, resolves
    /// its single candidate Phase through the catalog, owns the unscaled draw
    /// target picked once per phase, and the live length-modifier math.
    ///
    /// Slot resolution never consumes RNG. Exactly one candidate executes;
    /// zero candidates is invalid and more than one is a deliberate
    /// not-yet-implemented error.
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
        private readonly ContentCatalog _catalog;
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

        /// <summary>Title of the current slot, or null once complete (host display convenience).</summary>
        public string CurrentSlotTitle => IsComplete ? null : CurrentSlot.Title;

        /// <summary>Title of the current resolved phase, or null once complete (host display convenience).</summary>
        public string CurrentPhaseTitle => IsComplete ? null : CurrentPhase.Title;

        public SessionDriver(
            SessionDefinition session,
            ContentCatalog catalog,
            Func<float> lengthModifier = null,
            IGameLog log = null,
            IRandomSource phaseRng = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _lengthModifier = lengthModifier ?? (() => 1f);
            _log = log ?? new NullGameLog();
            _rng = phaseRng ?? new SystemRandomSource();
            _phaseIndex = 0;
            _baseTarget = NextBaseTarget(_rng);
            CurrentMustInclude = ToArray(CurrentPhase.MustIncludeTags);
            CurrentMustExclude = ToArray(CurrentPhase.MustExcludeTags);
        }

        private PhaseSlotDefinition CurrentSlot
        {
            get
            {
                if (_phaseIndex < 0 || _phaseIndex >= _session.PhaseSlots.Count)
                {
                    throw new InvalidOperationException("SessionDriver: no current phase slot.");
                }
                return _session.PhaseSlots[_phaseIndex];
            }
        }

        private PhaseDefinition CurrentPhase
        {
            get
            {
                var slot = CurrentSlot;
                var candidates = slot.Candidates;
                if (candidates == null || candidates.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"SessionDriver: PhaseSlot '{slot.Id}' of session '{_session.Title}' has zero candidates; a slot must have exactly one candidate.");
                }
                if (candidates.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"SessionDriver: PhaseSlot '{slot.Id}' of session '{_session.Title}' has {candidates.Count} candidates; multi-candidate selection is not implemented yet.");
                }
                return _catalog.PhaseById(candidates[0].PhaseId);
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

            if (_phaseIndex >= _session.PhaseSlots.Count)
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
