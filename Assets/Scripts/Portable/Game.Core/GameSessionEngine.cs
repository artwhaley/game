using System;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Session-run facade over the two-level graph VM. Tickets 07-09 replaced the
    /// slot-era internals with the Phase VM + Session VM; this facade constructs
    /// the session VM on first advance, forwards host events, and exposes the
    /// one-card-per-request user-paced operation to hosts.
    /// </summary>
    public sealed class GameSessionEngine
    {
        private readonly GameContentDefinition _content;
        private readonly ContentCatalog _catalog;
        private readonly SessionDefinition _session;
        private readonly CoreServices _services;
        private readonly BackgroundActionTracker _tracker;
        private readonly PhaseRunRngFactory _rngFactory;

        private SessionGraphVm _vm;
        private bool _busy;

        public Player Player { get; }

        /// <summary>Session-global temperatures, initialized per the spawn options (Ticket 06).</summary>
        public TemperatureState Temperatures { get; }

        public event Action<CardDefinition> CardStarted;
        public event Action<CardDefinition> CardFinished;
        public event Action<string> PhaseEntered;
        public event Action SessionCompleted;

        public GameSessionEngine(GameContentDefinition content, string sessionId, CoreServices services)
            : this(content, sessionId, services, SessionSpawnOptions.Default)
        {
        }

        public GameSessionEngine(GameContentDefinition content, string sessionId, CoreServices services, SessionSpawnOptions spawn)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (string.IsNullOrEmpty(sessionId)) throw new ArgumentNullException(nameof(sessionId));
            _services = services ?? throw new ArgumentNullException(nameof(services));

            _content = content;
            _catalog = new ContentCatalog(content);
            _session = _catalog.SessionById(sessionId);
            _tracker = new BackgroundActionTracker(_services.Log);
            _rngFactory = new PhaseRunRngFactory();
            Player = new Player("Player");
            Temperatures = new TemperatureState(_catalog, spawn ?? SessionSpawnOptions.Default);
        }

        public ContentCatalog Catalog => _catalog;

        public string SessionId => _session.Id;
        public string SessionTitle => _session.Title;

        /// <summary>Phase id of the currently-active phase run (null before the first advance).</summary>
        public string CurrentPhaseId => _vm?.CurrentPhaseId;

        /// <summary>Phase-local progress of the currently-active run (0 before the first advance).</summary>
        public float CurrentProgress => _vm?.CurrentProgress ?? 0f;

        // ---------- host-observable state ----------

        public bool IsComplete { get; private set; }
        public bool IsBusy => _busy;

        /// <summary>Active background ("continuous") work; hosts may drain on shutdown.</summary>
        public Task DrainBackgroundAsync() => _tracker.DrainAsync();

        /// <summary>How many background actions are still running.</summary>
        public int PendingBackgroundCount => _tracker.ActiveCount;

        // ---------- the user-paced operation ----------

        /// <summary>
        /// Advances at most one executed Card per request through the session VM:
        /// prompts/decisions/cutscenes inside the step are awaited, and the VM is
        /// driven until exactly one card executes or the session completes. Busy
        /// requests are ignored; runtime content errors surface loudly.
        /// </summary>
        public async Task<AdvanceResult> AdvanceOneCardAsync(CancellationToken cancellationToken)
        {
            if (IsComplete)
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
                var vm = EnsureVm();
                var context = new ActionExecutionContext(
                    Player, _services, _catalog, Temperatures, null, ActionOwnerScope.All);

                while (!vm.IsComplete)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await vm.AdvanceAsync(context, cancellationToken);
                    switch (result.Outcome)
                    {
                        case SessionAdvanceOutcome.CardExecuted:
                            return new AdvanceResult(AdvanceResultKind.CardCompleted, result.Card);
                        case SessionAdvanceOutcome.YieldedForCard:
                            continue; // park at next card boundary and try again
                        case SessionAdvanceOutcome.SessionCompleted:
                            IsComplete = true;
                            return new AdvanceResult(AdvanceResultKind.SessionCompleted);
                        case SessionAdvanceOutcome.Error:
                            throw new InvalidOperationException(
                                $"Session '{_session.Id}' runtime error: " + result.ErrorMessage);
                        default:
                            throw new InvalidOperationException(
                                $"Session '{_session.Id}' produced unknown outcome '{result.Outcome}'.");
                    }
                }

                IsComplete = true;
                return new AdvanceResult(AdvanceResultKind.SessionCompleted);
            }
            finally
            {
                _busy = false;
            }
        }

        private SessionGraphVm EnsureVm()
        {
            if (_vm != null) return _vm;
            _vm = new SessionGraphVm(_content, _session.Id, _services, _tracker, _rngFactory);
            _vm.CardStarted += card => CardStarted?.Invoke(card);
            _vm.CardFinished += card => CardFinished?.Invoke(card);
            _vm.PhaseEntered += title => PhaseEntered?.Invoke(title);
            _vm.SessionCompleted += () => SessionCompleted?.Invoke();
            return _vm;
        }
    }
}
