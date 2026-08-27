using System;
using System.Collections.ObjectModel;
using System.IO;
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
    /// Thin reference player shell over the canonical SQLite content DB. INTERIM
    /// Graph Workbench state: content loads through the portable snapshot path and
    /// this window remains the seed of the Workbench (Tickets 12+ build out the
    /// Nodify four-pane authoring surfaces here). Actual playback is suspended
    /// while the graph VM lands (tickets 07-09); advancing reports that loudly.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int MaxLogEntries = 400;

        private readonly ObservableCollection<string> _log = new ObservableCollection<string>();
        private GameContentDefinition _content;
        private GameSessionEngine _engine;
        private CancellationTokenSource _sessionCts;
        private float _lengthModifier = 1f;

        public MainWindow()
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
        private static string ResolveDatabasePath()
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
                SetStatus("Starting…");
                DrawNextButton.IsEnabled = false;
                Log($"Session started: {selected.Title} (length {_lengthModifier:0.0}x, fixed seeds)");

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

        private async Task AdvanceAsync()
        {
            if (_engine == null || _sessionCts == null) return;

            DrawNextButton.IsEnabled = false;
            try
            {
                var result = await _engine.AdvanceOneCardAsync(_sessionCts.Token);

                switch (result.Kind)
                {
                    case AdvanceResultKind.CardCompleted:
                        RefreshProgress();
                        SetStatus("Done — draw next when ready");
                        DrawNextButton.IsEnabled = true;
                        break;
                    case AdvanceResultKind.SessionCompleted:
                        RefreshProgress();
                        SetStatus("Complete");
                        CardTitle.Text = result.Card?.Title ?? "(no card — skipped empty phases)";
                        DrawNextButton.IsEnabled = false;
                        Log("SESSION COMPLETE");
                        break;
                    case AdvanceResultKind.BusyIgnored:
                        Log("Draw ignored (advance already running).");
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
                RefreshProgress();
            };
            _engine.PhaseEntered += phaseTitle =>
            {
                PhaseTitle.Text = string.IsNullOrEmpty(phaseTitle) ? "(unnamed)" : phaseTitle;
                Log($"phase entered: {phaseTitle}");
            };
            _engine.SessionCompleted += () =>
            {
                SetStatus("Complete");
            };
        }

        private void RefreshProgress()
        {
            if (_engine == null) return;
            // Slot-era targets/remaining are gone; run-state visualization returns
            // with the graph VM preview (Docs/GraphWorkbench ticket 19).
            ProgressText.Text = _engine.IsComplete ? "complete" : "—";
        }

        // ---------- live length modifier ----------

        private void OnLengthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _lengthModifier = (float)e.NewValue;
            if (LengthLabel != null) // slider fires during XAML parse, before names resolve
            {
                LengthLabel.Text = $"{_lengthModifier:0.0}x";
            }
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
            CancelSession();
            base.OnClosed(e);
        }

        // ---------- host services bound to this window ----------

        private sealed class UiPromptService : IPromptService
        {
            private readonly MainWindow _window;
            public UiPromptService(MainWindow window) => _window = window;

            public Task<int?> AskAsync(string prompt, System.Collections.Generic.IReadOnlyList<string> options, CancellationToken cancellationToken)
                => _window.ShowChoiceAsync(prompt, options, cancellationToken);
        }

        private sealed class UiCutsceneService : ICutsceneService
        {
            private readonly MainWindow _window;
            public UiCutsceneService(MainWindow window) => _window = window;

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
