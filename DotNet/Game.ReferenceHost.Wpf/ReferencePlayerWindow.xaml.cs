using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        private int _seed;
        private ToyActivityHostService _toyHost;
        private readonly ReferencePlayerExecutionPauseGate _pauseGate;
        private readonly List<Action> _engineUnsubscribers = new List<Action>();
        private bool _runStarted;

        /// <summary>The visible integer seed used for the next/restarted run.</summary>
        public int Seed
        {
            get => _seed;
            set
            {
                _seed = value;
                if (SeedBox != null) SeedBox.Text = value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Raised on the UI thread whenever playback enters a phase (phase id).</summary>
        public event Action<string> PhaseChanged;
        public event Action<string> SessionNodeChanged;
        public event Action<string, string> PhaseNodeChanged;
        public event Action<GraphEdgeTraversal> EdgeTraversed;
        public event Action<CardDefinition> CardStarted;
        public event Action RunStarted;
        public event Action RunCompleted;
        public event Action RunStopped;

        public Func<ExecutionCheckpoint, string> AutomaticPauseReason
        {
            get => _pauseGate.AutomaticPauseReason;
            set => _pauseGate.AutomaticPauseReason = value;
        }

        public Func<ExecutionCheckpoint, Task<bool>> ResumeGuard
        {
            get => _pauseGate.ResumeGuard;
            set => _pauseGate.ResumeGuard = value;
        }

        public bool IsPaused => _pauseGate.IsPaused;

        public ReferencePlayerWindow()
        {
            InitializeComponent();
            LogList.ItemsSource = _log;
            _pauseGate = new ReferencePlayerExecutionPauseGate();
            _pauseGate.StateChanged += OnPauseStateChanged;
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
                    ConnectionInitializer.Initialize(connection);
                    CoreMigrator.EnsureSchema(connection);
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
        private static SessionSpawnOptions SpawnOptionsForRun(int seed)
        {
            return new SessionSpawnOptions(seed);
        }

        /// <summary>
        /// A missing profile DB is a legitimate empty state. An existing
        /// profile DB that cannot be opened, migrated, or read is a loud
        /// authoring/runtime error rather than an empty-profile fallback.
        /// </summary>
        private static CardSelectionProfile LoadSelectionProfile()
        {
            return CardSelectionProfile.FromProfile(UserProfileSelectionLoader.LoadSnapshot());
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
        /// argument, else the SQLITE_CANONICAL_DB test override, else the
        /// repo-relative dev path Content/GameContent.db. Never a hardcoded
        /// machine path.
        /// </summary>
        internal static string ResolveDatabasePath()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i + 1 < args.Length; i++)
            {
                if (args[i] == "--db") return Path.GetFullPath(args[i + 1]);
            }

            var testOverride = Environment.GetEnvironmentVariable("SQLITE_CANONICAL_DB");
            if (!string.IsNullOrWhiteSpace(testOverride)) return Path.GetFullPath(testOverride);

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Content", "GameContent.db"));
        }

        // ---------- session lifecycle ----------

        /// <summary>
        /// The Workbench simulates presentation: it validates and logs a
        /// performance decision with the same accept/reject contract Unity
        /// honours. Without a generated catalog there is nothing to plan
        /// against, so no host is registered and a Perform action fails loudly
        /// rather than pretending to have staged something.
        /// </summary>
        private IPerformanceHost CreateSimulatedPerformanceHost()
        {
            var catalog = PresentationCatalogLoader.TryLoad(ResolveDatabasePath());
            if (catalog == null)
            {
                Log("No generated presentation catalog next to the content database; " +
                    "run TruthCardGame/Performance/Generate Presentation Catalog in Unity " +
                    "before playing a Card that performs.");
                return null;
            }

            var initial = PresentationCatalogLoader.TryDefaultInitialState(catalog);
            if (initial == null)
            {
                Log("The generated presentation catalog declares no usable anchor; " +
                    "performance simulation is unavailable for this run.");
                return null;
            }

            Log($"Simulated presentation host ready: start {initial}, " +
                $"{catalog.Anchors.Count} anchor(s), {catalog.Ingredients.Count} ingredient(s).");
            return new SimulatedPerformanceHostService(catalog, initial, Log);
        }

        private async void OnStartSession(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_content == null || SessionCombo.SelectedItem is not SessionDefinition selected)
                {
                    Log("No session selected.");
                    return;
                }

                if (!TryReadSeed(out var seed)) return;
                _seed = seed;

                StopAuto();
                CancelSession();
                _pauseGate.Cancel();
                _sessionCts = new CancellationTokenSource();
                _waitingForContinue = false;

                _toyHost?.Dispose();
                _toyHost = new ToyActivityHostService();

                var services = new CoreServices(
                    delay: new WpfGameDelay(),
                    log: new UiGameLog(Log),
                    prompts: new UiPromptService(this),
                    cutscene: new UiCutsceneService(this),
                    toyActivity: _toyHost,
                    dialog: new DialogHostService((text, ct) => ShowDialogAsync(text, ct), new UiGameLog(Log)),
                    pauseGate: _pauseGate,
                    performance: CreateSimulatedPerformanceHost());

                UnsubscribeEngine();
                _engine = new GameSessionEngine(_content, selected.Id, services, SpawnOptionsForRun(seed),
                    rngFactory: null, selectionProfile: LoadSelectionProfile());

                SubscribeEngine();
                _runStarted = true;
                RunStarted?.Invoke();

                ClearInteractionArea();
                SessionTitle.Text = selected.Title;
                PhaseTitle.Text = "—";
                CardTitle.Text = "—";
                CardBodyText.Text = "—";
                ProgressText.Text = "—";
                TemperaturesText.Text = RefreshTemperatures();
                SetStatus("Starting…");
                DrawNextButton.IsEnabled = false;
                PauseButton.IsEnabled = true;
                ResumeButton.IsEnabled = false;
                Log($"Seed: {seed}");
                Log($"Session started: {SelectionDiagnosticsFormatter.Session(selected)}");

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

        private bool TryReadSeed(out int seed)
        {
            if (int.TryParse(SeedBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
            {
                return true;
            }

            SetStatus("Invalid seed");
            Log("ERROR: Seed must be a signed 32-bit integer.");
            return false;
        }

        private void OnRandomizeSeed(object sender, RoutedEventArgs e)
        {
            Seed = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
            Log($"Seed randomized: {Seed}");
        }

        private async void OnDrawNext(object sender, RoutedEventArgs e)
        {
            await AdvanceAsync();
        }

        private void OnPause(object sender, RoutedEventArgs e)
        {
            if (_engine == null || _engine.IsComplete || IsPaused) return;
            _pauseGate.RequestPause();
        }

        private async void OnResume(object sender, RoutedEventArgs e)
        {
            if (!IsPaused) return;
            try
            {
                if (!await _pauseGate.ResumeAsync())
                    SetStatus("Cannot resume — resolve the pause reason first.");
            }
            catch (Exception ex)
            {
                SetStatus("Resume error");
                Log("ERROR resuming: " + ex.Message);
            }
        }

        private void OnHalt(object sender, RoutedEventArgs e)
        {
            Log("RUNNER HALT requested.");
            StopAuto();
            _pauseGate.Cancel();
            CancelSession();
            RunStopped?.Invoke();
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
                    if (!IsPaused) SetStatus("Paused — continue when ready");
                }
            }
        }

        private void StopAuto()
        {
            _autoCts?.Cancel();
        }

        private async Task AdvanceAsync()
        {
            if (_engine == null || _sessionCts == null || IsPaused) return;

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
                        PauseButton.IsEnabled = false;
                        ResumeButton.IsEnabled = false;
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
            UnsubscribeEngine();
            var engine = _engine;
            Action<CardDefinition> cardStarted = card =>
            {
                CardTitle.Text = card.Title;
                CardBodyText.Text = card.BodyText ?? "";
                SetStatus("Executing…");
                LogCardStarted(card);
                CardStarted?.Invoke(card);
            };
            engine.CardStarted += cardStarted;
            _engineUnsubscribers.Add(() => engine.CardStarted -= cardStarted);

            Action<CardDefinition> cardFinished = card =>
            {
                Log($"card finished: {card.Title}");
                RefreshRunState();
            };
            engine.CardFinished += cardFinished;
            _engineUnsubscribers.Add(() => engine.CardFinished -= cardFinished);

            Action<string> phaseEntered = phaseId =>
            {
                RefreshRunState();
                Log($"phase entered: {PhaseDisplayName(phaseId)}");
                PhaseChanged?.Invoke(phaseId);
            };
            engine.PhaseEntered += phaseEntered;
            _engineUnsubscribers.Add(() => engine.PhaseEntered -= phaseEntered);

            Action sessionCompleted = () =>
            {
                SetStatus("Complete");
                ProgressText.Text = "complete";
                RunCompleted?.Invoke();
            };
            engine.SessionCompleted += sessionCompleted;
            _engineUnsubscribers.Add(() => engine.SessionCompleted -= sessionCompleted);

            Action<VariableCheckNodeDefinition, float, bool> variableCheck = (check, value, result) =>
                Log($"check: {check.VariableKey ?? check.SourceKind.ToString()} value={value:0.###} => {result}");
            engine.SessionVm.VariableCheckEvaluated += variableCheck;
            _engineUnsubscribers.Add(() => engine.SessionVm.VariableCheckEvaluated -= variableCheck);

            Action<string> runtimeError = message => Log("runtime error: " + message);
            engine.SessionVm.RuntimeError += runtimeError;
            _engineUnsubscribers.Add(() => engine.SessionVm.RuntimeError -= runtimeError);

            Action<string> sessionNodeChanged = nodeId =>
            {
                Log("session node: " + nodeId);
                SessionNodeChanged?.Invoke(nodeId);
            };
            engine.SessionVm.SessionNodeChanged += sessionNodeChanged;
            _engineUnsubscribers.Add(() => engine.SessionVm.SessionNodeChanged -= sessionNodeChanged);

            Action<string> phaseNodeChanged = nodeId =>
            {
                Log("phase node: " + nodeId);
                PhaseNodeChanged?.Invoke(engine.CurrentPhaseId, nodeId);
            };
            engine.SessionVm.PhaseNodeChanged += phaseNodeChanged;
            _engineUnsubscribers.Add(() => engine.SessionVm.PhaseNodeChanged -= phaseNodeChanged);

            Action<GraphEdgeTraversal> edgeTraversed = edge =>
            {
                Log($"edge: {edge.GraphKind} {edge.GraphOwnerId} {edge.EdgeId} {edge.SourceOutputId} → {edge.TargetNodeId}");
                EdgeTraversed?.Invoke(edge);
            };
            engine.EdgeTraversed += edgeTraversed;
            _engineUnsubscribers.Add(() => engine.EdgeTraversed -= edgeTraversed);

            Action<CardSelector.SelectionResult> cardSelectionEvaluated = evaluation =>
            {
                foreach (var candidate in evaluation.Candidates)
                {
                    if (candidate.Reasons.Count > 0)
                        Log($"candidate rejected: {candidate.Card.Title} [{candidate.Card.Id}] — " +
                            string.Join("; ", candidate.Reasons.Select(reason => SelectionDiagnosticsFormatter.Rejection(_content, reason))));
                    else
                        Log($"candidate eligible: {SelectionDiagnosticsFormatter.Card(candidate.Card)} weight={candidate.Weight:0.###}");
                }
                if (evaluation.Selected != null)
                    Log($"card selected: {SelectionDiagnosticsFormatter.Card(evaluation.Selected)}");
            };
            engine.CardSelectionEvaluated += cardSelectionEvaluated;
            _engineUnsubscribers.Add(() => engine.CardSelectionEvaluated -= cardSelectionEvaluated);
        }

        private void UnsubscribeEngine()
        {
            for (var i = _engineUnsubscribers.Count - 1; i >= 0; i--)
            {
                try { _engineUnsubscribers[i](); }
                catch { /* a closing/replaced engine cannot make cleanup fail */ }
            }
            _engineUnsubscribers.Clear();
        }

        private string PhaseDisplayName(string phaseId)
        {
            var phase = TryPhase(phaseId);
            return phase != null ? $"{phase.Title} [{phaseId}]" : phaseId;
        }

        private void LogCardStarted(CardDefinition card)
        {
            Log("CARD START\nTitle: " + SelectionDiagnosticsFormatter.Card(card) + "\nBody:\n" + (card?.BodyText ?? ""));
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

        /// <summary>
        /// Dialog host boundary: presents one authored dialog line and waits for
        /// the player to accept it (Direct Dialog and Dialog From Tags both flow
        /// through here via DialogHostService).
        /// </summary>
        internal Task ShowDialogAsync(string text, CancellationToken ct)
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
                SetStatus("Dialog…");
                InteractionArea.Children.Add(new TextBlock
                {
                    Text = text ?? "",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                var continueButton = new Button
                {
                    Content = "Continue",
                    Width = 120,
                    Height = 28
                };
                continueButton.Click += (_, _) =>
                {
                    ClearInteractionArea();
                    completion.TrySetResult(true); // resolves exactly once
                };
                InteractionArea.Children.Add(continueButton);
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

        private void OnPauseStateChanged(bool paused, string reason)
        {
            RunOnUi(() =>
            {
                PauseButton.IsEnabled = _engine != null && !_engine.IsComplete && !paused;
                ResumeButton.IsEnabled = paused;
                DrawNextButton.IsEnabled = _engine != null && !paused && !_engine.IsComplete;
                if (paused) SetStatus(reason);
                else if (_engine != null && !_engine.IsComplete) SetStatus("Running");
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            StopAuto();
            _pauseGate.Cancel();
            CancelSession();
            UnsubscribeEngine();
            if (_runStarted && (_engine == null || !_engine.IsComplete)) RunStopped?.Invoke();
            _toyHost?.Dispose();
            _toyHost = null;
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
