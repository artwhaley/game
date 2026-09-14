using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;
using UnityEngine;

namespace TruthCardGame.Performance
{
    /// <summary>
    /// Ticket 04 vertical proof panel. It opens the canonical content database
    /// read-only, takes one consistent snapshot, releases SQLite, selects a
    /// normal Session through <see cref="SessionSelector"/>, and then runs the
    /// real <see cref="GameSessionEngine"/> with Unity's performance host.
    ///
    /// This remains a disposable showcase-scene panel rather than a second game
    /// loop: Core owns session/action/performance rules, while this component
    /// only supplies Unity services and the small Play/Stop/Repeat surface used
    /// to verify the vertical slice. Repeat always reloads both SQLite content
    /// and the generated presentation catalog.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceSmokePanel : MonoBehaviour
    {
        private const string DatabaseRelativePath = "Content/GameContent.db";

        [SerializeField] private UnityPerformanceHost _host;
        [Tooltip("Leave empty to choose the first authored SessionType through SessionSelector.")]
        [SerializeField] private string sessionTypeId = "";
        [SerializeField] private int seed = 7;

        private GameSessionEngine _engine;
        private UnityPerformanceDialogService _dialog;
        private CancellationTokenSource _sessionCts;
        private Vector2 _dialogScroll;
        private Vector2 _decisionScroll;
        private bool _busy;
        private bool _waitingForContinue;
        private bool _sessionComplete;
        private string _selectedSessionTitle = "(none)";
        private string _lastOutcome = "(idle)";

        public void Bind(UnityPerformanceHost sourceHost)
        {
            _host = sourceHost;
        }

        private void Start()
        {
            if (_host == null) _host = GetComponent<UnityPerformanceHost>();
            if (_host == null)
            {
                _lastOutcome = "no performance host bound";
                return;
            }

            _ = StartSessionAsync();
        }

        private async Task StartSessionAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await StopExistingEngineAsync("repeat");
                _host.InvalidateCatalog();
                _host.ResetToInitialPlacement();

                var content = LoadCanonicalSnapshotReadOnly();
                var contentCatalog = new ContentCatalog(content);
                var session = SelectSession(content, contentCatalog);

                _dialog = new UnityPerformanceDialogService();
                _sessionCts = new CancellationTokenSource();
                var services = new CoreServices(
                    delay: new UnityGameDelay(),
                    log: new UnityGameLog(),
                    dialog: _dialog,
                    performance: _host);

                _engine = new GameSessionEngine(
                    content,
                    session.Id,
                    services,
                    new SessionSpawnOptions(seed));
                _selectedSessionTitle = session.Title;
                _waitingForContinue = false;
                _sessionComplete = false;
                _lastOutcome = "loaded session '" + session.Title + "' from SQLite; starting Core";

                _engine.CardStarted += card =>
                    _lastOutcome = "card started: " + (card?.Title ?? "(untitled)");
                _engine.CardFinished += card =>
                    _lastOutcome = "card finished: " + (card?.Title ?? "(untitled)");

                await AdvanceCoreAsync(continueAfterYield: false);
            }
            catch (OperationCanceledException)
            {
                ReleaseAfterCancellation();
                _lastOutcome = "stopped";
            }
            catch (Exception ex)
            {
                _lastOutcome = "startup/runtime failure: " + ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task ContinueSessionAsync()
        {
            if (_busy || !_waitingForContinue || _engine == null) return;
            _busy = true;
            try
            {
                _waitingForContinue = false;
                await AdvanceCoreAsync(continueAfterYield: true);
            }
            catch (OperationCanceledException)
            {
                ReleaseAfterCancellation();
                _lastOutcome = "stopped";
            }
            catch (Exception ex)
            {
                _lastOutcome = "runtime failure: " + ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task AdvanceCoreAsync(bool continueAfterYield)
        {
            if (_engine == null || _sessionCts == null)
                throw new InvalidOperationException("the performance session was not initialized");

            var result = continueAfterYield
                ? await _engine.ContinueAsync(_sessionCts.Token)
                : await _engine.RunUntilYieldAsync(_sessionCts.Token);

            switch (result.Kind)
            {
                case AdvanceResultKind.WaitForContinue:
                    _waitingForContinue = true;
                    _lastOutcome = "waiting for Continue after the tagged dialogue";
                    break;
                case AdvanceResultKind.SessionCompleted:
                    _sessionComplete = true;
                    _waitingForContinue = false;
                    _lastOutcome = "session completed; performance host was stopped by Core";
                    break;
                case AdvanceResultKind.BusyIgnored:
                    _lastOutcome = "Core ignored a duplicate advance while busy";
                    break;
                default:
                    throw new InvalidOperationException("unknown Core advance result '" + result.Kind + "'");
            }
        }

        /// <summary>Stop is available while a staging clip is still awaiting host acknowledgement.</summary>
        private void RequestStop()
        {
            if (_busy)
            {
                _sessionCts?.Cancel();
                _lastOutcome = "stopping...";
                return;
            }

            _ = StopIdleSessionAsync();
        }

        private async Task StopIdleSessionAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await StopExistingEngineAsync("panel stop");
                _host?.ResetToInitialPlacement();
                _waitingForContinue = false;
                _sessionComplete = false;
                _lastOutcome = "stopped";
            }
            catch (Exception ex)
            {
                _lastOutcome = "stop failed: " + ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task StopExistingEngineAsync(string reason)
        {
            _sessionCts?.Cancel();
            if (_engine != null)
            {
                await _engine.ShutdownAsync(reason);
                _engine = null;
            }

            _sessionCts?.Dispose();
            _sessionCts = null;
        }

        private void ReleaseAfterCancellation()
        {
            _engine = null;
            _sessionCts?.Dispose();
            _sessionCts = null;
            _waitingForContinue = false;
            _host?.ResetToInitialPlacement();
        }

        private SessionDefinition SelectSession(GameContentDefinition content, ContentCatalog catalog)
        {
            var orderedTypes = (content.SessionTypes ?? new List<SessionTypeDefinition>())
                .Where(type => type != null && !string.IsNullOrEmpty(type.Id))
                .OrderBy(type => type.SortOrder)
                .ThenBy(type => type.Id, StringComparer.Ordinal)
                .ToList();
            var requestedTypeId = string.IsNullOrWhiteSpace(sessionTypeId)
                ? orderedTypes.FirstOrDefault()?.Id
                : sessionTypeId;
            if (string.IsNullOrEmpty(requestedTypeId))
                throw new InvalidOperationException("the canonical content snapshot declares no SessionType to select");

            // The showcase uses the same empty capability profile as the
            // fixture's normal launcher. Keeping the eligibility predicate in
            // this path means a future required capability fails at selection
            // instead of being silently bypassed by the performance panel.
            var profile = new CardSelectionProfile();
            var eligibility = new SessionTypeEligibility(catalog);
            if (!eligibility.Evaluate(requestedTypeId, profile).IsEligible)
                throw new InvalidOperationException(
                    "SessionType '" + requestedTypeId + "' is not eligible for the showcase profile");

            var selector = new SessionSelector(catalog);
            if (!selector.TrySelect(
                    requestedTypeId,
                    SeededRandomDomains.CreateSessionSelection(seed),
                    out var selected,
                    candidate => eligibility.Evaluate(candidate.SessionTypeId, profile).IsEligible))
            {
                throw new InvalidOperationException(
                    "SessionSelector found no eligible session for SessionType '" + requestedTypeId + "'");
            }
            return selected;
        }

        /// <summary>
        /// Reads one committed SQLite snapshot and closes the connection before
        /// any Core or Unity playback begins. The loader itself is shared with
        /// WPF and Unity; no game-content JSON or duplicate DTOs are involved.
        /// </summary>
        private static GameContentDefinition LoadCanonicalSnapshotReadOnly()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var databasePath = Path.Combine(
                projectRoot, DatabaseRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(databasePath))
            {
                throw new FileNotFoundException(
                    "canonical content database not found at " + databasePath, databasePath);
            }

            Batteries_V2.Init();
            using (var connection = new SqliteConnection(
                       "Data Source=" + databasePath + ";Mode=ReadOnly;Pooling=False"))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var storedVersion = ReadSchemaVersion(connection);
                    if (storedVersion != CoreMigrations.MaxVersion)
                    {
                        throw new InvalidOperationException(
                            "canonical content schema is v" + storedVersion + "; Unity expects v" +
                            CoreMigrations.MaxVersion + ". Run the content migration before playing.");
                    }

                    var content = GameContentSnapshotLoader.Load(connection, ensureSchema: false);
                    transaction.Commit();
                    return content;
                }
            }
        }

        private static int ReadSchemaVersion(DbConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MAX(version) FROM core_schema_migration;";
                var value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                    throw new InvalidOperationException("canonical content database has no schema version");
                return Convert.ToInt32(value);
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 340, 440, 500), GUI.skin.box);
            GUILayout.Label("Ticket 04: canonical session performance");
            GUILayout.Label("SQLite snapshot -> SessionSelector -> Core -> Unity host");
            GUILayout.Label("Session: " + _selectedSessionTitle);

            GUI.enabled = !_busy && _engine == null;
            if (GUILayout.Button("Play session")) _ = StartSessionAsync();
            GUI.enabled = !_busy;
            if (GUILayout.Button("Repeat (reload SQLite + catalog)")) _ = StartSessionAsync();
            GUI.enabled = !_busy && _waitingForContinue;
            if (GUILayout.Button("Continue")) _ = ContinueSessionAsync();
            GUI.enabled = true;
            if (_dialog != null && _dialog.IsPending && GUILayout.Button("Acknowledge dialogue"))
                _dialog.Acknowledge();
            if (GUILayout.Button("Stop")) RequestStop();

            GUILayout.Space(8);
            GUILayout.Label("Status: " + _lastOutcome);
            if (_sessionComplete) GUILayout.Label("Completed — Repeat reads a fresh database snapshot.");

            GUILayout.Space(4);
            GUILayout.Label("Tagged dialogue history:");
            _dialogScroll = GUILayout.BeginScrollView(_dialogScroll, GUILayout.MinHeight(120));
            if (_dialog != null)
            {
                foreach (var line in _dialog.History) GUILayout.Label(line);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.Label("Host decisions (newest last):");
            _decisionScroll = GUILayout.BeginScrollView(_decisionScroll, GUILayout.MinHeight(100));
            if (_host != null)
            {
                foreach (var decision in _host.Decisions) GUILayout.Label(decision);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            _sessionCts?.Cancel();
            if (_engine != null) _ = _engine.ShutdownAsync("performance panel destroyed");
            _sessionCts?.Dispose();
            _sessionCts = null;
        }

        /// <summary>
        /// The Unity tester intentionally paces the two ordinary tagged lines
        /// with an acknowledgement button. The final Continue is still the
        /// separate graph yield; WPF keeps its immediate presentation contract.
        /// </summary>
        private sealed class UnityPerformanceDialogService : IDialogService
        {
            public readonly List<string> History = new List<string>();
            private PendingDialog _pending;

            public bool IsPending => _pending != null;

            public Task ShowAsync(string text, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                History.Add(text ?? "");

                if (_pending != null)
                    throw new InvalidOperationException("Unity performance dialogue already awaits acknowledgement");

                var pending = new PendingDialog();
                _pending = pending;
                if (cancellationToken.CanBeCanceled)
                {
                    pending.Registration = cancellationToken.Register(() =>
                    {
                        if (ReferenceEquals(_pending, pending)) _pending = null;
                        pending.Completion.TrySetCanceled(cancellationToken);
                    });
                }
                return pending.Completion.Task;
            }

            public void Acknowledge()
            {
                var pending = _pending;
                if (pending == null) return;
                _pending = null;
                pending.Registration.Dispose();
                pending.Completion.TrySetResult(true);
            }

            private sealed class PendingDialog
            {
                public readonly TaskCompletionSource<bool> Completion =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                public CancellationTokenRegistration Registration;
            }
        }
    }
}
