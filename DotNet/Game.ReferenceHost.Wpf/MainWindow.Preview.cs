using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Ticket 19: live Core preview / graph debugger. The collapsible bottom
    /// strip runs the REAL Core VM (GameSessionEngine) against a FRESH SQLite
    /// snapshot taken at Start/Restart — DB edits during a run never mutate the
    /// running snapshot. The SessionVm's fine-grained events drive highlights
    /// on both canvases (current session/phase node ring, transfer edge, check
    /// branch) and the compact readouts (temperatures, progress, phase, node,
    /// card, continuation stack, PhaseRun identity, runtime errors).
    /// </summary>
    public partial class MainWindow
    {
        private GameSessionEngine _previewEngine;
        private GameContentDefinition _previewContent;
        private CancellationTokenSource _previewCts;
        private readonly ObservableCollection<string> _previewLog = new ObservableCollection<string>();
        private bool _previewVmSubscribed;
        private bool _previewWaitingForContinue;

        // ---------- strip toggle ----------

        private void OnTogglePreview(object sender, RoutedEventArgs e)
        {
            var show = PreviewStrip.Visibility != Visibility.Visible;
            PreviewStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PreviewToggleButton.Content = show ? "Preview ▾" : "Preview ▸";
            if (show && _previewLog.Count == 0)
            {
                PreviewLogList.ItemsSource = _previewLog;
            }
        }

        // ---------- lifecycle ----------

        private async void OnPreviewStart(object sender, RoutedEventArgs e)
        {
            var session = _vm.SelectedSession;
            if (session == null)
            {
                PreviewStatus("Select a session in the Library first.");
                return;
            }
            StopPreview();
            ClearPreviewHighlights();
            _previewVmSubscribed = false;
            _previewWaitingForContinue = false;
            _previewLog.Clear();
            try
            {
                // Fresh snapshot: load once at start; later DB edits are invisible to the run.
                var path = ReferencePlayerWindow.ResolveDatabasePath();
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    _previewContent = GameContentSnapshotLoader.Load(connection);
                }
                _previewCts = new CancellationTokenSource();
                _previewWaitingForContinue = false;

                var services = new CoreServices(
                    delay: new PreviewDelay(),
                    log: new PreviewLogSink(PreviewLog),
                    prompts: new PreviewPromptService(this),
                    cutscene: new PreviewCutsceneService(this));

                if (!TryReadPreviewSeed(out var seed)) return;
                _previewEngine = new GameSessionEngine(_previewContent, session.Id, services, SpawnOptionsForRun(seed),
                    rngFactory: null, selectionProfile: CardSelectionProfile.FromProfile(LoadProfileSnapshot()));
                SubscribePreviewVm(); // eager VM: wire the trace before the first advance
                PreviewSessionLabel.Text = session.Title + "  (fresh snapshot)";
                PreviewErrorText.Text = "";
                PreviewDrawButton.IsEnabled = false;
                PreviewLog($"Seed: {seed}");
                PreviewLog("Session started: " + session.Title + " (fresh snapshot)");
                await AdvancePreviewAsync();
            }
            catch (Exception ex)
            {
                PreviewStatus("Error");
                PreviewLog("ERROR starting: " + ex.Message);
            }
        }

        private async void OnPreviewAdvance(object sender, RoutedEventArgs e)
        {
            await AdvancePreviewAsync();
        }

        private async void OnPreviewRestart(object sender, RoutedEventArgs e)
        {
            // Restart = fresh snapshot + new engine, exactly like Start.
            var session = _vm.SelectedSession;
            if (session == null)
            {
                PreviewStatus("Select a session first.");
                return;
            }
            StopPreview();
            ClearPreviewHighlights();
            _previewVmSubscribed = false;
            _previewWaitingForContinue = false;
            try
            {
                var path = ReferencePlayerWindow.ResolveDatabasePath();
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    _previewContent = GameContentSnapshotLoader.Load(connection);
                }
                _previewCts = new CancellationTokenSource();
                _previewWaitingForContinue = false;
                var services = new CoreServices(
                    delay: new PreviewDelay(),
                    log: new PreviewLogSink(PreviewLog),
                    prompts: new PreviewPromptService(this),
                    cutscene: new PreviewCutsceneService(this));
                if (!TryReadPreviewSeed(out var seed)) return;
                _previewEngine = new GameSessionEngine(_previewContent, session.Id, services, SpawnOptionsForRun(seed),
                    rngFactory: null, selectionProfile: CardSelectionProfile.FromProfile(LoadProfileSnapshot()));
                SubscribePreviewVm();
                PreviewErrorText.Text = "";
                PreviewDrawButton.IsEnabled = false;
                PreviewLog($"Seed: {seed}");
                PreviewLog("Restarted (fresh snapshot).");
                await AdvancePreviewAsync();
            }
            catch (Exception ex)
            {
                PreviewStatus("Error");
                PreviewLog("ERROR restarting: " + ex.Message);
            }
        }

        private void OnPreviewStop(object sender, RoutedEventArgs e)
        {
            StopPreview();
            PreviewStatus("Stopped");
            PreviewLog("Stopped.");
            ClearPreviewHighlights();
            RefreshPreviewState();
        }

        /// <summary>
        /// Session start options (Ticket 20 spawn seam). Defaults today; a
        /// future profile loader supplies TemperatureOverrides here.
        /// </summary>
        private static SessionSpawnOptions SpawnOptionsForRun(int seed)
        {
            return new SessionSpawnOptions(seed);
        }

        private bool TryReadPreviewSeed(out int seed)
        {
            if (int.TryParse(PreviewSeedBox.Text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out seed))
            {
                return true;
            }

            PreviewStatus("Invalid seed");
            PreviewLog("ERROR: Seed must be a signed 32-bit integer.");
            return false;
        }

        private void OnRandomizePreviewSeed(object sender, RoutedEventArgs e)
        {
            PreviewSeedBox.Text = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            PreviewLog("Seed randomized: " + PreviewSeedBox.Text);
        }

        private void StopPreview()
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = null;
            _previewEngine = null;
            _previewContent = null;
            _previewVmSubscribed = false;
            _previewWaitingForContinue = false;
            PreviewDrawButton.IsEnabled = false;
        }

        private async Task AdvancePreviewAsync()
        {
            if (_previewEngine == null || _previewCts == null) return;
            PreviewDrawButton.IsEnabled = false;
            try
            {
                var result = _previewWaitingForContinue
                    ? await _previewEngine.ContinueAsync(_previewCts.Token)
                    : await _previewEngine.RunUntilYieldAsync(_previewCts.Token);
                RefreshPreviewState();
                switch (result.Kind)
                {
                    case AdvanceResultKind.WaitForContinue:
                        _previewWaitingForContinue = true;
                        PreviewStatus("Waiting — press Continue");
                        PreviewDrawButton.IsEnabled = true;
                        break;
                    case AdvanceResultKind.SessionCompleted:
                        _previewWaitingForContinue = false;
                        PreviewStatus("Complete");
                        PreviewDrawButton.IsEnabled = false;
                        PreviewLog("SESSION COMPLETE");
                        break;
                    case AdvanceResultKind.BusyIgnored:
                        PreviewStatus("Busy (advance already running)");
                        PreviewDrawButton.IsEnabled = true;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                PreviewStatus("Stopped");
            }
            catch (Exception ex)
            {
                PreviewStatus("Error");
                PreviewErrorText.Text = ex.Message;
                PreviewLog("ERROR: " + ex.Message);
            }
        }

        // ---------- VM event wiring (fine-grained trace) ----------

        private void SubscribePreviewVm()
        {
            if (_previewVmSubscribed) return;
            var vm = _previewEngine?.SessionVm;
            if (vm == null) return;
            _previewVmSubscribed = true;

            vm.SessionNodeChanged += nodeId => RunOnUi(() =>
            {
                UpdateSessionHighlight(nodeId);
                RefreshPreviewState();
            });
            vm.PhaseEntered += phaseId => RunOnUi(() =>
            {
                PreviewLog("phase entered: " + PhaseDisplayName(phaseId));
                RefreshPreviewState();
            });
            vm.PhaseNodeChanged += nodeId => RunOnUi(() =>
            {
                UpdatePhaseHighlight(nodeId);
                RefreshPreviewState();
            });
            vm.CardStarted += card => RunOnUi(() =>
            {
                PreviewCardText.Text = card.Title;
                PreviewLog("CARD START\nTitle: " + (card?.Title ?? "") + "\nBody:\n" + (card?.BodyText ?? ""));
            });
            vm.VariableCheckEvaluated += (node, value, passed) => RunOnUi(() =>
            {
                UpdateCheckHighlight(node, passed);
                PreviewLog($"check {node.Id}: {value:0.#} {(passed ? "≥ pass" : "fail")}");
            });
            vm.RuntimeError += message => RunOnUi(() =>
            {
                PreviewErrorText.Text = message;
                PreviewLog("RUNTIME ERROR: " + message);
            });
            vm.SessionCompleted += () => RunOnUi(() =>
            {
                PreviewStatus("Complete");
                PreviewLog("SESSION COMPLETE");
            });

            // Seed from the current (pre-first-advance) state.
            UpdateSessionHighlight(vm.CurrentSessionNodeId);
        }

        // ---------- graph highlighting ----------

        private void ClearPreviewHighlights()
        {
            foreach (var node in _vm.SessionGraph.Nodes) node.DebugActive = false;
            foreach (var node in _vm.PhaseGraph.Nodes) node.DebugActive = false;
            foreach (var connection in _vm.SessionGraph.Connections) connection.DebugActive = false;
            foreach (var connection in _vm.PhaseGraph.Connections) connection.DebugActive = false;
        }

        private void UpdateSessionHighlight(string nodeId)
        {
            foreach (var node in _vm.SessionGraph.Nodes)
            {
                node.DebugActive = node.Id == nodeId;
            }
            // Transfer edge = the edge entering the current node.
            foreach (var connection in _vm.SessionGraph.Connections)
            {
                connection.DebugActive = connection.Target?.Owner?.Id == nodeId;
            }
        }

        private void UpdatePhaseHighlight(string nodeId)
        {
            foreach (var node in _vm.PhaseGraph.Nodes)
            {
                node.DebugActive = node.Id == nodeId;
            }
            foreach (var connection in _vm.PhaseGraph.Connections)
            {
                connection.DebugActive = false;
            }
        }

        private void UpdateCheckHighlight(VariableCheckNodeDefinition check, bool passed)
        {
            if (check?.Outputs == null) return;
            var wantedOutput = check.Outputs.FirstOrDefault(o => (o.Kind == GraphPortKind.True) == passed)?.Id;
            foreach (var connection in _vm.PhaseGraph.Connections)
            {
                connection.DebugActive = connection.Source?.Id == wantedOutput;
            }
        }

        // ---------- readouts ----------

        private void RefreshPreviewState()
        {
            var engine = _previewEngine;
            if (engine == null)
            {
                PreviewPhaseText.Text = "—";
                PreviewNodeText.Text = "—";
                PreviewProgressText.Text = "—";
                PreviewStackText.Text = "depth 0";
                PreviewPhaseRunText.Text = "—";
                PreviewTemperaturesText.Text = "—";
                return;
            }
            var vm = engine.SessionVm;
            var phaseId = engine.CurrentPhaseId;
            var phase = _previewContent?.Phases.FirstOrDefault(p => p.Id == phaseId);

            PreviewPhaseText.Text = phase != null ? $"{phase.Title} [{phaseId}]" : (phaseId ?? "—");
            PreviewNodeText.Text = vm != null && !string.IsNullOrEmpty(vm.CurrentSessionNodeId) ? vm.CurrentSessionNodeId : "—";
            PreviewProgressText.Text = phaseId != null
                ? $"{engine.CurrentProgress:0} / {ProgressTargetFor(phase):0.#}"
                : "—";
            PreviewTemperaturesText.Text = PreviewTemperatures(engine);

            if (vm != null)
            {
                var summary = vm.ContinuationSummary();
                var frames = string.Join(" → ", summary.Select(f => f.PhaseId ?? "?"));
                PreviewStackText.Text = "depth " + vm.ContinuationDepth + (summary.Count > 0 ? " — " + frames : "");
                PreviewPhaseRunText.Text = vm.CurrentPhaseId != null
                    ? $"placement {vm.CurrentPlacementNodeId ?? "—"} → phase {vm.CurrentPhaseId}"
                    : "—";
            }
        }

        /// <summary>First PhaseProgress VariableCheck's target, or null when the phase has none.</summary>
        private float? ProgressTargetFor(PhaseDefinition phase)
        {
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

        private string PreviewTemperatures(GameSessionEngine engine)
        {
            var parts = engine.Catalog.TemperaturesList
                .Select(t =>
                {
                    try
                    {
                        return $"{(string.IsNullOrEmpty(t.Title) ? t.Id : t.Title)}: {engine.Temperatures.Get(t.Id):0.#}";
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

        private string PhaseDisplayName(string phaseId)
        {
            var phase = _previewContent?.Phases.FirstOrDefault(p => p.Id == phaseId);
            return phase != null ? $"{phase.Title} [{phaseId}]" : phaseId;
        }

        // ---------- prompt / cutscene interaction (strip) ----------

        internal Task<int?> ShowPreviewChoiceAsync(string prompt, IReadOnlyList<string> options, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ct.CanBeCanceled)
            {
                var registration = ct.Register(() =>
                {
                    RunOnUi(() =>
                    {
                        ClearPreviewInteraction();
                        completion.TrySetCanceled(ct);
                    });
                });
                completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            }
            RunOnUi(() =>
            {
                ClearPreviewInteraction();
                PreviewStatus("Waiting for choice…");
                PreviewInteractionArea.Children.Add(new TextBlock
                {
                    Text = prompt,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                });
                for (var i = 0; i < options.Count; i++)
                {
                    var index = i;
                    var button = new Button
                    {
                        Content = options[index],
                        Height = 24,
                        Margin = new Thickness(0, 3, 0, 0),
                    };
                    button.Click += (_, _) =>
                    {
                        ClearPreviewInteraction();
                        completion.TrySetResult(index);
                    };
                    PreviewInteractionArea.Children.Add(button);
                }
            });
            return completion.Task;
        }

        internal Task ShowPreviewCutsceneAsync(string resourceId, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ct.CanBeCanceled)
            {
                var registration = ct.Register(() =>
                {
                    RunOnUi(() =>
                    {
                        ClearPreviewInteraction();
                        completion.TrySetCanceled(ct);
                    });
                });
                completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            }
            RunOnUi(() =>
            {
                ClearPreviewInteraction();
                PreviewStatus("Cutscene…");
                PreviewInteractionArea.Children.Add(new TextBlock
                {
                    Text = "CUTSCENE",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 2),
                });
                PreviewInteractionArea.Children.Add(new TextBlock
                {
                    Text = resourceId,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                });
                var complete = new Button { Content = "Complete Cutscene", Height = 24, Width = 160 };
                complete.Click += (_, _) =>
                {
                    ClearPreviewInteraction();
                    completion.TrySetResult(true);
                };
                PreviewInteractionArea.Children.Add(complete);
            });
            return completion.Task;
        }

        private void ClearPreviewInteraction()
        {
            PreviewInteractionArea.Children.Clear();
        }

        private async Task ShowPreviewTimedCutsceneAsync(string resourceId, CancellationToken ct)
        {
            RunOnUi(() =>
            {
                ClearPreviewInteraction();
                PreviewStatus("Cutscene...");
                PreviewInteractionArea.Children.Add(new TextBlock
                {
                    Text = "CUTSCENE\n" + resourceId,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                });
            });
            PreviewLog("CUTSCENE START " + resourceId);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                PreviewLog("CUTSCENE FINISHED " + resourceId);
                RunOnUi(ClearPreviewInteraction);
            }
            catch
            {
                RunOnUi(ClearPreviewInteraction);
                PreviewLog("CUTSCENE CANCELED " + resourceId);
                throw;
            }
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.Invoke(action);
        }

        private void PreviewLog(string message)
        {
            RunOnUi(() =>
            {
                RunnerLogBuffer.Append(_previewLog, $"[{DateTime.Now:HH:mm:ss}] {message}");
                if (PreviewLogList.Items.Count > 0)
                {
                    PreviewLogList.ScrollIntoView(PreviewLogList.Items.GetItemAt(PreviewLogList.Items.Count - 1));
                }
            });
        }

        private void PreviewStatus(string status)
        {
            RunOnUi(() => PreviewStatusText.Text = status);
        }

        private void OnPreviewCopyLog(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RunnerLogBuffer.Export(_previewLog));
                PreviewStatus("Log copied");
            }
            catch (Exception ex)
            {
                PreviewStatus("Copy failed");
                PreviewLog("ERROR copying log: " + ex.Message);
            }
        }

        private void OnPreviewSaveLog(object sender, RoutedEventArgs e)
        {
            var title = SanitizePreviewFileName(_vm.SelectedSession?.Title ?? "Session");
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save preview log",
                Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = title + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log",
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, RunnerLogBuffer.Export(_previewLog), new UTF8Encoding(false));
                PreviewStatus("Log saved");
            }
            catch (Exception ex)
            {
                PreviewStatus("Save failed");
                PreviewLog("ERROR saving log: " + ex.Message);
            }
        }

        private void OnPreviewClearLog(object sender, RoutedEventArgs e)
        {
            RunnerLogBuffer.Clear(_previewLog);
            PreviewStatus("Log cleared");
        }

        private static string SanitizePreviewFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder();
            foreach (var character in value ?? "Session")
                builder.Append(invalid.Contains(character) ? '_' : character);
            var result = builder.ToString().Trim();
            return result.Length == 0 ? "Session" : result;
        }

        // ---------- preview host services ----------

        private sealed class PreviewPromptService : IPromptService
        {
            private readonly MainWindow _window;
            public PreviewPromptService(MainWindow window) => _window = window;

            public Task<int?> AskAsync(string prompt, IReadOnlyList<string> options, CancellationToken cancellationToken)
                => _window.ShowPreviewChoiceAsync(prompt, options, cancellationToken);
        }

        private sealed class PreviewCutsceneService : ICutsceneService
        {
            private readonly MainWindow _window;
            public PreviewCutsceneService(MainWindow window) => _window = window;

            public Task PlayAsync(string resourceId, CancellationToken cancellationToken)
                => _window.ShowPreviewTimedCutsceneAsync(resourceId, cancellationToken);
        }

        private sealed class PreviewDelay : IGameDelay
        {
            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
                => Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }

        private sealed class PreviewLogSink : IGameLog
        {
            private readonly Action<string> _sink;
            public PreviewLogSink(Action<string> sink) => _sink = sink;

            public void Info(string message) => _sink(message);
            public void Warning(string message) => _sink("[warn] " + message);
            public void Error(string message) => _sink("[error] " + message);
        }
    }
}
