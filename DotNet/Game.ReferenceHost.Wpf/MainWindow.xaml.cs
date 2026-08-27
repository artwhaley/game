using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Graph Workbench shell (ticket 12): four horizontal panes — Library
    /// (18*), Session Graph (32*), Phase Graph (32*), Inspector (18*) — with
    /// three draggable splitters. Both graph panes host real Nodify editors
    /// (pan/zoom/select/move/connect) wired to canonical content view models.
    /// Pane ratios persist across launches; the reference player lives behind
    /// the "Run Session" preview command and reports the playing phase back
    /// for graph parity.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly WorkbenchViewModel _vm;
        private bool _loaded;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new WorkbenchViewModel();
            DataContext = _vm;

            NodeDoubleClickCommand = new DelegateCommand<GraphNodeViewModel>(OnNodeDoubleClicked);

            // Inspector pane follows whichever editor has a selection.
            _vm.SessionGraph.SelectionChanged += (_, _) => UpdateInspector(_vm.SessionGraph.SelectedNode);
            _vm.PhaseGraph.SelectionChanged += (_, _) => UpdateInspector(_vm.PhaseGraph.SelectedNode);
        }

        /// <summary>Double-click on a PhaseReference node opens that phase below.</summary>
        public ICommand NodeDoubleClickCommand { get; }

        private void OnNodeDoubleClicked(GraphNodeViewModel node)
        {
            if (node == null) return;
            if (node.Kind == "phase-reference" && !string.IsNullOrEmpty(node.RefId))
            {
                if (_vm.SelectPhaseById(node.RefId))
                {
                    SyncPhaseListSelection(node.RefId);
                    StatusText.Text = "Phase opened from session graph: " + node.Title;
                }
                else
                {
                    StatusText.Text = "Referenced phase not found: " + node.RefId;
                }
            }
        }

        /// <summary>Selects the phase in the Library list without re-triggering the change handler.</summary>
        private void SyncPhaseListSelection(string phaseId)
        {
            var match = _vm.Content?.Phases.FirstOrDefault(p => p.Id == phaseId);
            if (match == null) return;
            PhaseList.SelectionChanged -= OnPhaseListChanged;
            try
            {
                PhaseList.SelectedItem = match;
                PhaseList.ScrollIntoView(match);
            }
            finally
            {
                PhaseList.SelectionChanged += OnPhaseListChanged;
            }
        }

        private void UpdateInspector(GraphNodeViewModel node)
        {
            if (node == null)
            {
                InspTitle.Text = "(none)";
                InspId.Text = "";
                InspKind.Text = "";
                return;
            }
            InspTitle.Text = node.Title;
            InspId.Text = string.IsNullOrEmpty(node.RefId)
                ? "id: " + node.Id
                : "id: " + node.Id + "\nref: " + node.RefId;
            InspKind.Text = node.Kind + (string.IsNullOrEmpty(node.Subtitle) ? "" : "\n" + node.Subtitle);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;

            // Restore persisted pane ratios before the first layout pass.
            var persisted = LayoutPersistence.Load(LayoutPersistence.DefaultSettingsPath());
            _vm.Layout.SetRatios(
                persisted.LibraryRatio, persisted.SessionRatio,
                persisted.PhaseRatio, persisted.InspectorRatio);

            ApplyLayout();
            LoadContent();
        }

        // ---------- content loading (canonical SQLite) ----------

        private void LoadContent()
        {
            try
            {
                var path = ReferencePlayerWindow.ResolveDatabasePath();
                GameContentDefinition content;
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    content = GameContentSnapshotLoader.Load(connection);
                }
                _vm.LoadContent(content);
                BindLibrary();
                StatusText.Text = $"Content loaded from {path} — {content.Sessions.Count} sessions · {content.Phases.Count} phases";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Content error";
                MessageBox.Show(this, "Failed to load content:\n\n" + ex.Message,
                    "Workbench", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BindLibrary()
        {
            SessionList.ItemsSource = _vm.Content.Sessions;
            SessionList.DisplayMemberPath = nameof(SessionDefinition.Title);
            if (SessionList.Items.Count > 0) SessionList.SelectedIndex = 0;

            PhaseList.ItemsSource = _vm.Content.Phases;
            PhaseList.DisplayMemberPath = nameof(PhaseDefinition.Title);
            if (PhaseList.Items.Count > 0) PhaseList.SelectedIndex = 0;
        }

        private void OnSessionListChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SessionList.SelectedItem is SessionDefinition session)
            {
                _vm.SelectSession(session);
                SessionHeader.Text = "Session Graph — " + session.Title;
            }
        }

        private void OnPhaseListChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PhaseList.SelectedItem is PhaseDefinition phase)
            {
                _vm.SelectPhase(phase);
                PhaseHeader.Text = "Phase Graph — " + phase.Title;
            }
        }

        // ---------- layout (GridSplitters + persisted ratios) ----------

        /// <summary>
        /// Applies the persisted ratios to the four content columns (indices
        /// 0, 2, 4, 6) as star weights, leaving the Auto splitters between
        /// them. Star sizing keeps proportions correct as the window resizes.
        /// </summary>
        private void ApplyLayout()
        {
            SetContentStarWeights(new[]
            {
                _vm.Layout.LibraryRatio,
                _vm.Layout.SessionRatio,
                _vm.Layout.PhaseRatio,
                _vm.Layout.InspectorRatio,
            });
        }

        private void SetContentStarWeights(double[] weights)
        {
            var contentColumns = new[]
            {
                PaneGrid.ColumnDefinitions[0],
                PaneGrid.ColumnDefinitions[2],
                PaneGrid.ColumnDefinitions[4],
                PaneGrid.ColumnDefinitions[6],
            };
            for (var i = 0; i < 4; i++)
            {
                contentColumns[i].Width = new GridLength(weights[i], GridUnitType.Star);
            }
        }

        private void OnSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            // Convert the dragged pixel layout back to ratios, persist, and
            // re-express as stars so window resizes keep the new proportions.
            PersistCurrentRatios();
            SaveLayout();
            ApplyLayout();
        }

        private void OnResetLayout(object sender, RoutedEventArgs e)
        {
            _vm.Layout.SetRatios(
                WorkbenchLayout.DefaultLibraryRatio,
                WorkbenchLayout.DefaultSessionRatio,
                WorkbenchLayout.DefaultPhaseRatio,
                WorkbenchLayout.DefaultInspectorRatio);
            SaveLayout();
            ApplyLayout();
            StatusText.Text = "Layout reset to 18/32/32/18";
        }

        private void SaveLayout()
        {
            LayoutPersistence.Save(LayoutPersistence.DefaultSettingsPath(), _vm.Layout);
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            // Persist current splitter positions as ratios before exit.
            try
            {
                PersistCurrentRatios();
                SaveLayout();
            }
            catch
            {
                // Never block shutdown over settings persistence.
            }
        }

        private void PersistCurrentRatios()
        {
            var widths = new[]
            {
                PaneGrid.ColumnDefinitions[0].ActualWidth,
                PaneGrid.ColumnDefinitions[2].ActualWidth,
                PaneGrid.ColumnDefinitions[4].ActualWidth,
                PaneGrid.ColumnDefinitions[6].ActualWidth,
            };
            var total = 0.0;
            for (var i = 0; i < 4; i++) total += widths[i];
            if (total <= 0) return;
            _vm.Layout.SetRatios(widths[0] / total, widths[1] / total, widths[2] / total, widths[3] / total);
        }

        // ---------- reference player preview (with graph parity) ----------

        private void OnRunSession(object sender, RoutedEventArgs e)
        {
            var player = new ReferencePlayerWindow { Owner = this };
            // Parity: while the player runs, the Phase Graph follows the phase
            // being played so the two views stay in sync.
            player.PhaseChanged += phaseId =>
            {
                if (_vm.SelectPhaseById(phaseId))
                {
                    SyncPhaseListSelection(phaseId);
                }
            };
            player.Show();
        }

        // ---------- palette actions (unsaved authoring nodes) ----------

        private void OnAddSessionStart(object sender, RoutedEventArgs e)
        {
            if (_vm.SessionHasStart())
            {
                StatusText.Text = "Session already has a Start node (singular).";
                return;
            }
            var node = _vm.SessionGraph.AddNode(
                "start-" + (_vm.SessionGraph.Nodes.Count + 1),
                "Start", "Session entry", "start", new Point(40, 40));
            GraphEditorViewModel.Output(node, node.Id + "-out", "out", "normal", "");
            StatusText.Text = "Added Start node (unsaved).";
        }

        private void OnAddPhaseReference(object sender, RoutedEventArgs e)
        {
            var phase = PhaseList.SelectedItem as PhaseDefinition ?? _vm.SelectedPhase;
            var node = _vm.SessionGraph.AddPhaseReference(phase?.Id ?? "(phase)", new Point(80, 60));
            StatusText.Text = "Added Phase Reference node (unsaved).";
        }

        private void OnAddSessionDecision(object sender, RoutedEventArgs e)
        {
            _vm.SessionGraph.AddSessionDecision(new Point(120, 60));
            StatusText.Text = "Added Session Decision node (unsaved).";
        }

        private void OnAddSessionEnd(object sender, RoutedEventArgs e)
        {
            _vm.SessionGraph.AddSessionEnd(new Point(160, 60));
            StatusText.Text = "Added Session End node (unsaved).";
        }

        private void OnAddPhaseEntry(object sender, RoutedEventArgs e)
        {
            if (_vm.PhaseHasEntry())
            {
                StatusText.Text = "Phase already has an Entry node (singular).";
                return;
            }
            _vm.PhaseGraph.AddNode(
                "entry-" + (_vm.PhaseGraph.Nodes.Count + 1),
                "Entry", "", "entry", new Point(40, 40));
            StatusText.Text = "Added Entry node (unsaved).";
        }

        private void OnAddCardExecutor(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddCardExecutor(new Point(80, 60)); StatusText.Text = "Added Draw Card node (unsaved)."; }
        private void OnAddVariableCheck(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddVariableCheck(new Point(120, 60)); StatusText.Text = "Added Check node (unsaved)."; }
        private void OnAddActionNode(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddActionNode(new Point(160, 60)); StatusText.Text = "Added Action node (unsaved)."; }
        private void OnAddPhaseDecision(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddPhaseDecision(new Point(200, 60)); StatusText.Text = "Added Decision node (unsaved)."; }
        private void OnAddReturn(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddReturn(new Point(240, 60)); StatusText.Text = "Added Return node (unsaved)."; }
    }
}