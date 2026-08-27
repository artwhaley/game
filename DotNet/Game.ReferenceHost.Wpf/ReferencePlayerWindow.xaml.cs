using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        private const int MaxLogEntries = 400;

        private readonly ObservableCollection<string> _log = new ObservableCollection<string>();
        private GameContentDefinition _content;
        private GameSessionEngine _engine;
        private CancellationTokenSource _sessionCts;
        private CancellationTokenSource _autoCts;

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

                var services = new CoreServices(
                    delay: new WpfGameDelay(),
                    log: new UiGameLog(Log),
                    prompts: new UiPromptService(this),
                    cutscene: new UiCutsceneService(this));

                _engine = new GameSessionEngine(_content, selected.Id, services);

                SubscribeEngine();

                ClearInteractionArea();
                SessionTitle.Text = selected.Title;
                PhaseTitle.Text = "—";
                CardTitle.Text = "—";
                ProgressText.Text = "—";
                TemperaturesText.Text = RefreshTemperatures();
                SetStatus("Starting…");
                DrawNextButton.IsEnabled = false;
                Log($"Session started: {selected.Title} (fixed seeds)");

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
                while (!_autoCts.IsCancellationRequested && _engine != null && !_engine.IsComplete)
                {
                    var result = await _engine.AdvanceOneCardAsync(_autoCts.Token);
                    RefreshRunState();
                    if (result.Kind == AdvanceResultKind.SessionCompleted) break;
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
                    SetStatus("Paused — draw next when ready");
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
                var result = await _engine.AdvanceOneCardAsync(_sessionCts.Token);
                RefreshRunState();

                switch (result.Kind)
                {
                    case AdvanceResultKind.CardCompleted:
                        SetStatus("Done — draw next when ready");
                        DrawNextButton.IsEnabled = true;
                        break;
                    case AdvanceResultKind.SessionCompleted:
                        SetStatus("Complete");
                        CardTitle.Text = result.Card?.Title ?? "(no card — skipped empty phases)";
                        DrawNextButton.IsEnabled = false;
                        Log("SESSION COMPLETE");
                        break;
                    case AdvanceResultKind.BusyIgnored:
                        Log("Draw ignored (advance already running).");
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
                SetStatus("Executing…");
                Log($"card started: {card.Title}");
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
        }

        private string PhaseDisplayName(string phaseId)
        {
            var phase = TryPhase(phaseId);
            return phase != null ? $"{phase.Title} [{phaseId}]" : phaseId;
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
                _log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                while (_log.Count > MaxLogEntries)
                {
                    _log.RemoveAt(0);
                }
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
                => _window.ShowCutsceneAsync(resourceId, cancellationToken);
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