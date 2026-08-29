using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Reference player shell over the canonical SQLite content DB. Kept alive
    /// from the Workbench behind the Preview command (ticket 12). Loads content
    /// through the portable snapshot path and plays a session through the
    /// graph VM (tickets 07-09). Surfaces run state legibly: phase titles,
    /// phase progress against the phase's progress-check target, temperature
    /// values, and a PhaseChanged event the workbench uses for graph parity.
    /// </summary>
    public partial class ReferencePlayerWindow : Window
    {
        private readonly ObservableCollection<string> _log = new ObservableCollection<string>();
        private GameContentDefinition _content;
        private GameSessionEngine _engine;
        private CancellationTokenSource _sessionCts;
        private CancellationTokenSource _autoCts;
        private bool _waitingForContinue;

        /// <summary>Raised on the UI thread whenever playback enters a phase (phase id).</summary>
        public event Action<string> PhaseChanged;

        public ReferencePlayerWindow()
        {
            InitializeComponent();
            LogList.ItemsSource = _log;
            Loaded += (_, _) => LoadContent();
        }

        // ---------- content loading (canonical SQLite) ----------

        private void LoadContent()
        {
            try
            {
                var path = ResolveDatabasePath();
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    _content = GameContentSnapshotLoader.Load(connection);
                }
                SessionCombo.ItemsSource = _content.Sessions;
                SessionCombo.DisplayMemberPath = nameof(SessionDefinition.Title);
                SessionCombo.SelectedIndex = 0;
                Log("Content loaded from " + path);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Content error";
                MessageBox.Show(this, "Failed to load content:\n\n" + ex.Message,
                    "Reference host", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Session start options (Ticket 20 spawn seam). Today this returns the
        /// defaults (Happiness 50 when the content defines no default); a future
        /// profile loader supplies SessionSpawnOptions.TemperatureOverrides here
        /// from UserProfilePaths.ProfileDatabasePath().
        /// </summary>
        private static SessionSpawnOptions SpawnOptionsForRun()
        {
            return SessionSpawnOptions.Default;
        }

        /// <summary>
        /// A missing profile DB is a legitimate empty state. An existing
        /// profile DB that cannot be opened, migrated, or read is a loud
        /// authoring/runtime error rather than an empty-profile fallback.
        /// </summary>
        private static CardSelectionProfile LoadSelectionProfile()
        {
            var path = UserProfilePaths.ProfileDatabasePath();
            if (!File.Exists(path)) return new CardSelectionProfile();
            using (var connection = new SqliteConnection("Data Source=" + path))
            {
                connection.Open();
                TruthCardGame.Profile.Sqlite.ProfileStore.EnsureSchema(connection);
                return TruthCardGame.Profile.Sqlite.ProfileStore.Load(connection).ToSelectionProfile();
            }
        }

        /// <summary>
        /// Ticket 16: consumer-style start — select and run a specific session
        /// (used by the Workbench's Play-by-Type flow after uniform selection).
        /// Self-sufficient: content loads here when the window was created
        /// before its Loaded event fired (the caller may not have Shown it yet).
        /// </summary>
        public void RunSession(string sessionId)
        {
            if (_content == null)
            {
                LoadContent();
                if (_content == null)
                {
                    throw new InvalidOperationException("Content could not be loaded; cannot run session " + sessionId);
                }
            }
            var session = _content.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null)
            {
                throw new InvalidOperationException("Session not found: " + sessionId);
            }
            SessionCombo.SelectedItem = session;
            OnStartSession(this, new RoutedEventArgs());
        }

        /// <summary>
        /// Canonical DB location: an explicit --db &lt;path&gt; command-line
        /// argument, else the repo-relative dev path Content/GameContent.db.
        /// Never a hardcoded machine path.
        /// </summary>
        internal static string ResolveDatabasePath()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i + 1 < args.Length; i++)
            {
                if (args[i] == "--db") return Path.GetFullPath(args[i + 1]);
            }

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Content", "GameContent.db"));
        }

        // ---------- session lifecycle ----------

        private async void OnStartSession(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_content == null || SessionCombo.SelectedItem is not SessionDefinition selected)
                {
                    Log("No session selected.");
                    return;
                }

                StopAuto();
                CancelSession();
                _sessionCts = new CancellationTokenSource();
                _waitingForContinue = false;

                var services = new CoreServices(
                    delay: new WpfGameDelay(),
                    log: new UiGameLog(Log),
                    prompts: new UiPromptService(this),
                    cutscene: new UiCutsceneService(this));

                _engine = new GameSessionEngine(_content, selected.Id, services, SpawnOptionsForRun(),
                    rngFactory: null, selectionProfile: LoadSelectionProfile());

                SubscribeEngine();

                ClearInteractionArea();
                SessionTitle.Text = selected.Title;
                PhaseTitle.Text = "—";
                CardTitle.Text = "—";
                CardBodyText.Text = "—";
                ProgressText.Text = "—";
                TemperaturesText.Text = RefreshTemperatures();
                SetStatus("Starting…");
                DrawNextButton.IsEnabled = false;
                Log($"Session started: {selected.Title}");

                await AdvanceAsync();
            }
            catch (OperationCanceledException)
            {
                Log("Session start canceled.");
            }
            catch (Exception ex)
            {
                // Invalid content must surface here, not as a dispatcher crash.
                SetStatus("Error");
                Log("ERROR starting session: " + ex.Message);
            }
        }

        private async void OnDrawNext(object sender, RoutedEventArgs e)
        {
            await AdvanceAsync();
        }

        private void OnHalt(object sender, RoutedEventArgs e)
        {
            Log("RUNNER HALT requested.");
            StopAuto();
            CancelSession();
            Close();
        }

        /// <summary>Auto-run: advances repeatedly until the session completes or toggled off.</summary>
        private async void OnAutoToggled(object sender, RoutedEventArgs e)
        {
            if (_autoCts != null)
            {
                StopAuto();
                return;
            }
            if (_engine == null || _sessionCts == null)
            {
                Log("Start a session before auto-running.");
                return;
            }

            _autoCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
            AutoButton.Content = "Stop ■";
            DrawNextButton.IsEnabled = false;
            SetStatus("Auto-running…");
            Log("Auto-run started.");
            try
            {
                var waitingForContinue = false;
                while (!_autoCts.IsCancellationRequested && _engine != null && !_engine.IsComplete)
                {
                    var result = waitingForContinue
                        ? await _engine.ContinueAsync(_autoCts.Token)
                        : await _engine.RunUntilYieldAsync(_autoCts.Token);
                    RefreshRunState();
                    if (result.Kind == AdvanceResultKind.SessionCompleted) break;
                    waitingForContinue = result.Kind == AdvanceResultKind.WaitForContinue;
                    await Task.Delay(300, _autoCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Log("Auto-run stopped.");
            }
            catch (Exception ex)
            {
                SetStatus("Error");
                Log("ERROR during auto-run: " + ex.Message);
            }
            finally
            {
                AutoButton.Content = "Auto ▶";
                _autoCts?.Dispose();
                _autoCts = null;
                if (_engine == null || !_engine.IsComplete)
                {
                    DrawNextButton.IsEnabled = _engine != null;
                    SetStatus("Paused — continue when ready");
                }
            }
        }

        private void StopAuto()
        {
            _autoCts?.Cancel();
        }

        private async Task AdvanceAsync()
        {
            if (_engine == null || _sessionCts == null) return;

            DrawNextButton.IsEnabled = false;
            try
            {
                var result = _waitingForContinue
                    ? await _engine.ContinueAsync(_sessionCts.Token)
                    : await _engine.RunUntilYieldAsync(_sessionCts.Token);
                RefreshRunState();

                switch (result.Kind)
                {
                    case AdvanceResultKind.WaitForContinue:
                        _waitingForContinue = true;
                        SetStatus("Waiting — press Continue");
                        DrawNextButton.IsEnabled = true;
                        break;
                    case AdvanceResultKind.SessionCompleted:
                        _waitingForContinue = false;
                        SetStatus("Complete");
                        CardTitle.Text = result.Card?.Title ?? "(no card — skipped empty phases)";
                        DrawNextButton.IsEnabled = false;
                        Log("SESSION COMPLETE");
                        break;
                    case AdvanceResultKind.BusyIgnored:
                        Log("Continue ignored (advance already running).");
                        DrawNextButton.IsEnabled = true;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                Log("Advance canceled (restart/shutdown).");
            }
            catch (Exception ex)
            {
                SetStatus("Error");
                Log("ERROR: " + ex.Message);
            }
        }

        /// <summary>Refreshes the phase/progress/temperature read-outs from engine state.</summary>
        private void RefreshRunState()
        {
            if (_engine == null) return;

            var phaseId = _engine.CurrentPhaseId;
            if (!string.IsNullOrEmpty(phaseId))
            {
                var phase = TryPhase(phaseId);
                PhaseTitle.Text = phase != null
                    ? $"{phase.Title}  [{phaseId}]"
                    : phaseId;
                ProgressText.Text = DescribeProgress(phaseId);
            }
            TemperaturesText.Text = RefreshTemperatures();
        }

        private string DescribeProgress(string phaseId)
        {
            var done = _engine.CurrentProgress;
            var target = ProgressTargetFor(phaseId);
            return target.HasValue ? $"{done:0} / {target.Value:0}" : $"{done:0} / ?";
        }

        /// <summary>Finds the phase's progress-check target (first PhaseProgress VariableCheck).</summary>
        private float? ProgressTargetFor(string phaseId)
        {
            var phase = TryPhase(phaseId);
            if (phase?.Graph?.Nodes == null) return null;
            foreach (var node in phase.Graph.Nodes)
            {
                if (node is VariableCheckNodeDefinition check &&
                    check.SourceKind == VariableSourceKind.PhaseProgress)
                {
                    return check.CompareValue;
                }
            }
            return null;
        }

        private PhaseDefinition TryPhase(string phaseId)
        {
            if (_content == null || string.IsNullOrEmpty(phaseId)) return null;
            foreach (var phase in _content.Phases)
            {
                if (phase.Id == phaseId) return phase;
            }
            return null;
        }

        private string RefreshTemperatures()
        {
            if (_engine == null || _content == null) return "—";
            var catalog = _engine.Catalog;
            var parts = catalog.TemperaturesList
                .Select(t =>
                {
                    try
                    {
                        return $"{(string.IsNullOrEmpty(t.Title) ? t.Id : t.Title)}: {_engine.Temperatures.Get(t.Id):0.#}";
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(s => s != null);
            var text = string.Join("   ", parts);
            return text.Length == 0 ? "(none defined)" : text;
        }

        private void SubscribeEngine()
        {
            _engine.CardStarted += card =>
            {
                CardTitle.Text = card.Title;
                CardBodyText.Text = card.BodyText ?? "";
                SetStatus("Executing…");
                LogCardStarted(card);
            };
            _engine.CardFinished += card =>
            {
                Log($"card finished: {card.Title}");
                RefreshRunState();
            };
            _engine.PhaseEntered += phaseId =>
            {
                RefreshRunState();
                Log($"phase entered: {PhaseDisplayName(phaseId)}");
                PhaseChanged?.Invoke(phaseId);
            };
            _engine.SessionCompleted += () =>
            {
                SetStatus("Complete");
                ProgressText.Text = "complete";
            };
            _engine.SessionVm.VariableCheckEvaluated += (check, value, result) =>
                Log($"check: {check.VariableKey ?? check.SourceKind.ToString()} value={value:0.###} => {result}");
            _engine.SessionVm.RuntimeError += message => Log("runtime error: " + message);
            _engine.SessionVm.SessionNodeChanged += nodeId => Log("session node: " + nodeId);
            _engine.SessionVm.PhaseNodeChanged += nodeId => Log("phase node: " + nodeId);
            _engine.CardSelectionEvaluated += evaluation =>
            {
                foreach (var candidate in evaluation.Candidates)
                {
                    if (candidate.Reasons.Count > 0)
                        Log($"candidate rejected: {candidate.Card.Title} [{candidate.Card.Id}] — " +
                            string.Join("; ", candidate.Reasons.Select(reason => reason.Describe())));
                    else
                        Log($"candidate eligible: {candidate.Card.Title} [{candidate.Card.Id}] weight={candidate.Weight:0.###}");
                }
                if (evaluation.Selected != null)
                    Log($"card selected: {evaluation.Selected.Title} [{evaluation.Selected.Id}]");
            };
        }

        private string PhaseDisplayName(string phaseId)
        {
            var phase = TryPhase(phaseId);
            return phase != null ? $"{phase.Title} [{phaseId}]" : phaseId;
        }

        private void LogCardStarted(CardDefinition card)
        {
            Log("CARD START\nTitle: " + (card?.Title ?? "") + "\nBody:\n" + (card?.BodyText ?? ""));
        }

        private void OnCopyLog(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RunnerLogBuffer.Export(_log));
                SetStatus("Log copied");
            }
            catch (Exception ex)
            {
                SetStatus("Copy failed");
                Log("ERROR copying log: " + ex.Message);
            }
        }

        private void OnSaveLog(object sender, RoutedEventArgs e)
        {
            var session = SessionCombo.SelectedItem as SessionDefinition;
            var title = SanitizeFileName(session?.Title ?? "Session");
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save execution log",
                Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = title + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log",
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, RunnerLogBuffer.Export(_log), new UTF8Encoding(false));
                SetStatus("Log saved");
            }
            catch (Exception ex)
            {
                SetStatus("Save failed");
                Log("ERROR saving log: " + ex.Message);
            }
        }

        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            RunnerLogBuffer.Clear(_log);
            SetStatus("Log cleared");
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder();
            foreach (var character in value ?? "Session")
                builder.Append(invalid.Contains(character) ? '_' : character);
            var result = builder.ToString().Trim();
            return result.Length == 0 ? "Session" : result;
        }

        // ---------- prompt / cutscene UI (host services) ----------

        internal Task<int?> ShowChoiceAsync(string prompt, System.Collections.Generic.IReadOnlyList<string> options, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (ct.CanBeCanceled)
            {
                var registration = ct.Register(() =>
                {
                    RunOnUi(ClearInteractionArea);
                    completion.TrySetCanceled(ct);
                });
                completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            }

            RunOnUi(() =>
            {
                ClearInteractionArea();
                SetStatus("Waiting for choice…");
                InteractionArea.Children.Add(new TextBlock
                {
                    Text = prompt,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });

                for (var i = 0; i < options.Count; i++)
                {
                    var index = i;
                    var button = new Button
                    {
                        Content = options[index],
                        Height = 26,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    button.Click += (_, _) =>
                    {
                        ClearInteractionArea();
                        completion.TrySetResult(index); // resolves exactly once
                    };
                    InteractionArea.Children.Add(button);
                }
            });

            return completion.Task;
        }

        internal Task ShowCutsceneAsync(string resourceId, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (ct.CanBeCanceled)
            {
                var registration = ct.Register(() =>
                {
                    RunOnUi(ClearInteractionArea);
                    completion.TrySetCanceled(ct);
                });
                completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            }

            RunOnUi(() =>
            {
                ClearInteractionArea();
                SetStatus("Waiting for cutscene…");
                InteractionArea.Children.Add(new TextBlock
                {
                    Text = "CUTSCENE",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                InteractionArea.Children.Add(new TextBlock
                {
                    Text = resourceId,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                var completeButton = new Button
                {
                    Content = "Complete Cutscene",
                    Width = 160,
                    Height = 28
                };
                completeButton.Click += (_, _) =>
                {
                    ClearInteractionArea();
                    completion.TrySetResult(true); // resolves exactly once
                };
                InteractionArea.Children.Add(completeButton);
            });

            return completion.Task;
        }

        private void ClearInteractionArea()
        {
            InteractionArea.Children.Clear();
        }

        private async Task ShowTimedCutsceneAsync(string resourceId, CancellationToken ct)
        {
            RunOnUi(() =>
            {
                ClearInteractionArea();
                SetStatus("Playing cutscene...");
                InteractionArea.Children.Add(new TextBlock
                {
                    Text = "CUTSCENE\n" + resourceId,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                });
            });
            Log($"CUTSCENE START {resourceId}");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                Log($"CUTSCENE FINISHED {resourceId}");
                RunOnUi(ClearInteractionArea);
            }
            catch
            {
                RunOnUi(ClearInteractionArea);
                Log($"CUTSCENE CANCELED {resourceId}");
                throw;
            }
        }

        // ---------- helpers ----------

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.Invoke(action);
        }

        private void SetStatus(string status)
        {
            RunOnUi(() => StatusText.Text = status);
        }

        private void Log(string message)
        {
            RunOnUi(() =>
            {
                RunnerLogBuffer.Append(_log, $"[{DateTime.Now:HH:mm:ss}] {message}");
                if (LogList.Items.Count > 0)
                {
                    LogList.ScrollIntoView(LogList.Items.GetItemAt(LogList.Items.Count - 1));
                }
            });
        }

        private void CancelSession()
        {
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = null;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopAuto();
            CancelSession();
            base.OnClosed(e);
        }

        // ---------- host services bound to this window ----------

        private sealed class UiPromptService : IPromptService
        {
            private readonly ReferencePlayerWindow _window;
            public UiPromptService(ReferencePlayerWindow window) => _window = window;

            public Task<int?> AskAsync(string prompt, System.Collections.Generic.IReadOnlyList<string> options, CancellationToken cancellationToken)
                => _window.ShowChoiceAsync(prompt, options, cancellationToken);
        }

        private sealed class UiCutsceneService : ICutsceneService
        {
            private readonly ReferencePlayerWindow _window;
            public UiCutsceneService(ReferencePlayerWindow window) => _window = window;

            public Task PlayAsync(string resourceId, CancellationToken cancellationToken)
                => _window.ShowTimedCutsceneAsync(resourceId, cancellationToken);
        }

        private sealed class WpfGameDelay : IGameDelay
        {
            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
                => Task.Delay(delay, cancellationToken);
        }

        private sealed class UiGameLog : IGameLog
        {
            private readonly Action<string> _sink;
            public UiGameLog(Action<string> sink) => _sink = sink;
            public void Info(string message) => _sink(message);
            public void Warning(string message) => _sink("[warn] " + message);
            public void Error(string message) => _sink("[error] " + message);
        }
    }
}
