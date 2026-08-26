using System;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// User-paced session orchestration: one AdvanceOneCardAsync call advances
    /// at most one ordinary drawn card, skipping no-match phases within the
    /// same request. Construction is inert — hosts invoke the first advance at
    /// session start (matching Unity's automatic first draw) and later only
    /// from explicit Draw Next input.
    ///
    /// Core owns all rules: phase targets, card eligibility, no-match phase
    /// advancement, blocking/nonblocking action sequencing, and completion.
    /// Hosts observe through the synchronous lifecycle events below and never
    /// reimplement game rules. The end-of-session presentation delay/scene
    /// change remains host behavior.
    /// </summary>
    public sealed class GameSessionEngine
    {
        private readonly SessionDriver _driver;
        private readonly CardSelector _selector;
        private readonly ActionExecutor _executor;
        private readonly BackgroundActionTracker _tracker;
        private readonly CoreServices _services;
        private readonly IRandomSource _cardRng;

        private bool _busy;

        public Player Player { get; }

        public event Action<CardDefinition> CardStarted;
        public event Action<CardDefinition> CardFinished;
        public event Action<int, int> PhaseChanged;
        public event Action SessionCompleted;

        public GameSessionEngine(
            SessionDefinition session,
            CardDeckDefinition deck,
            Func<float> lengthModifier,
            IRandomSource phaseRng,
            IRandomSource cardRng,
            CoreServices services)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (deck == null) throw new ArgumentNullException(nameof(deck));
            if (lengthModifier == null) throw new ArgumentNullException(nameof(lengthModifier));
            if (phaseRng == null) throw new ArgumentNullException(nameof(phaseRng));
            if (cardRng == null) throw new ArgumentNullException(nameof(cardRng));
            _services = services ?? throw new ArgumentNullException(nameof(services));

            _cardRng = cardRng;
            _tracker = new BackgroundActionTracker(_services.Log);
            _executor = new ActionExecutor(_tracker);
            _selector = new CardSelector(deck);
            _driver = new SessionDriver(session, lengthModifier, _services.Log, phaseRng);
            Player = new Player("Player");
        }

        // ---------- host-observable state ----------

        public bool IsComplete => _driver.IsComplete;
        public bool IsBusy => _busy;
        public int PhaseIndex => _driver.PhaseIndex;
        public string PhaseTitle => _driver.CurrentPhaseTitle;
        public int CurrentTarget() => _driver.CurrentTarget();
        public int Remaining() => _driver.Remaining();

        /// <summary>Active background ("continuous") work; hosts may drain on shutdown.</summary>
        public Task DrainBackgroundAsync() => _tracker.DrainAsync();

        /// <summary>How many background actions are still running.</summary>
        public int PendingBackgroundCount => _tracker.ActiveCount;

        // ---------- the one user-paced operation ----------

        public async Task<AdvanceResult> AdvanceOneCardAsync(CancellationToken cancellationToken)
        {
            if (_driver.IsComplete)
            {
                return new AdvanceResult(AdvanceResultKind.SessionCompleted);
            }
            if (_busy)
            {
                return new AdvanceResult(AdvanceResultKind.BusyIgnored);
            }
            _busy = true;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var found = _selector.TryDrawCard(
                        _driver.CurrentMustInclude,
                        _driver.CurrentMustExclude,
                        _cardRng,
                        out var card);

                    if (!found)
                    {
                        // No matching card: warn + advance, then retry within the same request.
                        var previous = _driver.PhaseIndex;
                        _driver.OnNoMatchingCard();
                        NotifyPhaseChanged(previous);
                        if (_driver.IsComplete)
                        {
                            SessionCompleted?.Invoke();
                            return new AdvanceResult(AdvanceResultKind.SessionCompleted);
                        }
                        continue;
                    }

                    CardStarted?.Invoke(card);

                    var context = new GameContext(Player, _services);
                    await _executor.ExecuteCardAsync(card, context, cancellationToken);

                    // Commit boundary: a cancelled advance must never emit
                    // completion events or progress the session, even if a
                    // host service swallowed its cancellation.
                    cancellationToken.ThrowIfCancellationRequested();

                    CardFinished?.Invoke(card);

                    var beforePhase = _driver.PhaseIndex;
                    _driver.OnCardCompleted();
                    NotifyPhaseChanged(beforePhase);

                    if (_driver.IsComplete)
                    {
                        SessionCompleted?.Invoke();
                        return new AdvanceResult(AdvanceResultKind.SessionCompleted, card);
                    }
                    return new AdvanceResult(AdvanceResultKind.CardCompleted, card);
                }
            }
            finally
            {
                _busy = false;
            }
        }

        private void NotifyPhaseChanged(int previous)
        {
            if (_driver.PhaseIndex != previous)
            {
                PhaseChanged?.Invoke(previous, _driver.PhaseIndex);
            }
        }
    }
}
