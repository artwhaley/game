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
    /// explicit-yield operation to hosts.
    /// </summary>
    public sealed class GameSessionEngine
    {
        private readonly GameContentDefinition _content;
        private readonly ContentCatalog _catalog;
        private readonly SessionDefinition _session;
        private readonly CoreServices _services;
        private readonly BackgroundActionTracker _tracker;
        private readonly PhaseRunRngFactory _rngFactory;
        private readonly CardSelectionProfile _selectionProfile;
        private readonly int _executionBudget;

        private SessionGraphVm _vm;
        private bool _busy;

        public Player Player { get; }

        /// <summary>Session-global temperatures, initialized per the spawn options (Ticket 06).</summary>
        public TemperatureState Temperatures { get; }

        public event Action<CardDefinition> CardStarted;
        public event Action<CardDefinition> CardFinished;
        public event Action<string> PhaseEntered;
        public event Action SessionCompleted;

        /// <summary>Forwarded from the session VM (selection diagnostics seam).</summary>
        public event Action<CardSelector.SelectionResult> CardSelectionEvaluated;

        public GameSessionEngine(GameContentDefinition content, string sessionId, CoreServices services)
            : this(content, sessionId, services, SessionSpawnOptions.Default)
        {
        }

        public GameSessionEngine(GameContentDefinition content, string sessionId, CoreServices services, SessionSpawnOptions spawn)
            : this(content, sessionId, services, spawn, null)
        {
        }

        public GameSessionEngine(GameContentDefinition content, string sessionId, CoreServices services,
            SessionSpawnOptions spawn, PhaseRunRngFactory rngFactory,
            int executionBudget = PhaseGraphVm.DefaultExecutionBudget,
            CardSelectionProfile selectionProfile = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (string.IsNullOrEmpty(sessionId)) throw new ArgumentNullException(nameof(sessionId));
            _services = services ?? throw new ArgumentNullException(nameof(services));

            _content = content;
            _catalog = new ContentCatalog(content);
            _session = _catalog.SessionById(sessionId);
            _tracker = new BackgroundActionTracker(_services.Log);
            SpawnOptions = spawn ?? SessionSpawnOptions.Default;
            _rngFactory = rngFactory ?? SeededRandomDomains.CreatePhaseRunFactory(SpawnOptions.Seed);
            _selectionProfile = selectionProfile ?? new CardSelectionProfile();
            if (executionBudget <= 0) throw new ArgumentOutOfRangeException(nameof(executionBudget));
            _executionBudget = executionBudget;
            Player = new Player("Player");
            Temperatures = new TemperatureState(_catalog, SpawnOptions);
            EnsureVm();
        }

        public ContentCatalog Catalog => _catalog;

        /// <summary>The spawn options this engine was started with (Ticket 20 seam).</summary>
        public SessionSpawnOptions SpawnOptions { get; }

        /// <summary>The visible integer seed that controls both deterministic RNG domains.</summary>
        public int Seed => SpawnOptions.Seed;

        public string SessionId => _session.Id;
        public string SessionTitle => _session.Title;

        /// <summary>Phase id of the currently-active phase run (null before the first advance).</summary>
        public string CurrentPhaseId => _vm?.CurrentPhaseId;

        /// <summary>Phase-local progress of the currently-active run (0 before the first advance).</summary>
        public float CurrentProgress => _vm?.CurrentProgress ?? 0f;

        // ---------- host-observable state ----------

        public bool IsComplete { get; private set; }
        public bool IsBusy => _busy;

        /// <summary>
        /// The inner session VM (Ticket 19 debugger). Null until the first
        /// first run call creates it; the VM raises the fine-grained
        /// node/check events the Workbench highlights against its canvases.
        /// </summary>
        public SessionGraphVm SessionVm => _vm;

        /// <summary>Active background ("continuous") work; hosts may drain on shutdown.</summary>
        public Task DrainBackgroundAsync() => _tracker.DrainAsync();

        /// <summary>How many background actions are still running.</summary>
        public int PendingBackgroundCount => _tracker.ActiveCount;

        // ---------- the explicit-yield operation ----------

        /// <summary>
        /// Runs automatically through the graph until an authored WaitForContinue,
        /// SessionEnd, cancellation, or a runtime/content error. Card completion
        /// is not a pause boundary; one run can execute multiple Cards.
        /// </summary>
        public Task<AdvanceResult> RunUntilYieldAsync(CancellationToken cancellationToken)
        {
            return RunUntilYieldCoreAsync(cancellationToken);
        }

        /// <summary>Resumes the same run after an authored WaitForContinue.</summary>
        public Task<AdvanceResult> ContinueAsync(CancellationToken cancellationToken)
        {
            return RunUntilYieldAsync(cancellationToken);
        }

        private async Task<AdvanceResult> RunUntilYieldCoreAsync(CancellationToken cancellationToken)
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
                    var result = await vm.RunUntilYieldAsync(context, cancellationToken);
                    switch (result.Outcome)
                    {
                        case SessionAdvanceOutcome.YieldedForContinue:
                            return new AdvanceResult(AdvanceResultKind.WaitForContinue);
                        case SessionAdvanceOutcome.SessionCompleted:
                            IsComplete = true;
                            return new AdvanceResult(AdvanceResultKind.SessionCompleted);
                        case SessionAdvanceOutcome.Error:
                            throw new GraphExecutionException(
                                $"Session '{_session.Id}' runtime error: " + result.ErrorMessage);
                        default:
                            throw new GraphExecutionException(
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
            _vm = new SessionGraphVm(_content, _session.Id, _services, _tracker, _rngFactory, _executionBudget, _selectionProfile);
            _vm.CardStarted += card => CardStarted?.Invoke(card);
            _vm.CardFinished += card => CardFinished?.Invoke(card);
            _vm.PhaseEntered += title => PhaseEntered?.Invoke(title);
            _vm.SessionCompleted += () => SessionCompleted?.Invoke();
            _vm.CardSelectionEvaluated += result => CardSelectionEvaluated?.Invoke(result);
            return _vm;
        }
    }
}
