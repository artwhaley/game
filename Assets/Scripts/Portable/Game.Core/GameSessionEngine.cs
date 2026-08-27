using System;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Session-run facade. INTERIM state of the Graph Workbench migration
    /// (Docs/GraphWorkbench/05-implementation-map.md): content construction and
    /// event surfaces exist so hosts keep compiling, while execution internals are
    /// being replaced by the two-level graph VM.
    ///
    /// - Ticket 06 installs Temperatures / SessionSpawnOptions / PhaseRun RNG seams.
    /// - Tickets 07-09 implement Phase-local stepping, GOTO/RETURN continuation,
    ///   and session-graph decisions on top; this facade then forwards real
    ///   semantics again and the advance methods stop throwing.
    /// </summary>
    public sealed class GameSessionEngine
    {
        private readonly ContentCatalog _catalog;
        private readonly SessionDefinition _session;
        private readonly CoreServices _services;
        private readonly BackgroundActionTracker _tracker;

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

            _catalog = new ContentCatalog(content);
            _session = _catalog.SessionById(sessionId);
            _tracker = new BackgroundActionTracker(_services.Log);
            Player = new Player("Player");
            Temperatures = new TemperatureState(_catalog, spawn ?? SessionSpawnOptions.Default);
        }

        public ContentCatalog Catalog => _catalog;

        public string SessionId => _session.Id;
        public string SessionTitle => _session.Title;

        // ---------- host-observable state ----------

        public bool IsComplete { get; private set; }
        public bool IsBusy => _busy;

        /// <summary>Active background ("continuous") work; hosts may drain on shutdown.</summary>
        public Task DrainBackgroundAsync() => _tracker.DrainAsync();

        /// <summary>How many background actions are still running.</summary>
        public int PendingBackgroundCount => _tracker.ActiveCount;

        // ---------- the user-paced operation ----------

        /// <summary>
        /// Advances at most one executed Card per request once the graph VM is in place.
        /// Until Tickets 07-09 land it fails loudly instead of pretending progress:
        /// no silent fake playback exists between the model and runtime tickets.
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
                cancellationToken.ThrowIfCancellationRequested();
                throw new NotSupportedException(
                    "Graph VM execution lands in Docs/GraphWorkbench tickets 07-09; " +
                    "session '" + _session.Id + "' cannot be advanced yet.");
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
