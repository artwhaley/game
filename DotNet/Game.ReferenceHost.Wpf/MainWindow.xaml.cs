using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    /// the "Run Session" preview command.
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
        /// Applies the persisted 18/32/32/18 ratios to the four content columns
        /// (indices 0, 2, 4, 6) as star weights, leaving the two Auto splitters
        /// between them. Ratio sum is used directly as weights, so dragging a
        /// splitter re-proportions the stars naturally.
        /// </summary>
        private void ApplyLayout()
        {
            var ratios = new[]
            {
                _vm.Layout.LibraryRatio,
                _vm.Layout.SessionRatio,
                _vm.Layout.PhaseRatio,
                _vm.Layout.InspectorRatio,
            };
            SetContentStarWeights(ratios);
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

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_loaded) ApplyLayout();
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

        // ---------- reference player preview ----------

        private void OnRunSession(object sender, RoutedEventArgs e)
        {
            var player = new ReferencePlayerWindow { Owner = this };
            player.Show();
        }

        // ---------- palette actions (unsaved authoring nodes) ----------

        private static Point CenterOf(GraphEditorViewModel editor, UIElement host)
        {
            var offset = 8.0;
            return new Point(offset, offset);
        }

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
            _vm.SessionGraph.Connect(node.Outputs[0], FindFirstSessionInput());
            StatusText.Text = "Added Start node (unsaved).";
        }

        private ConnectorViewModel FindFirstSessionInput()
        {
            foreach (var node in _vm.SessionGraph.Nodes)
            {
                if (node.Kind != "start" && node.Inputs.Count > 0) return node.Inputs[0];
            }
            return null;
        }

        private void OnAddPhaseReference(object sender, RoutedEventArgs e)
        {
            var node = _vm.SessionGraph.AddPhaseReference("(phase)", new Point(60, 60));
            StatusText.Text = "Added Phase Reference node (unsaved).";
        }

        private void OnAddSessionDecision(object sender, RoutedEventArgs e)
        {
            _vm.SessionGraph.AddSessionDecision(new Point(80, 60));
            StatusText.Text = "Added Session Decision node (unsaved).";
        }

        private void OnAddSessionEnd(object sender, RoutedEventArgs e)
        {
            _vm.SessionGraph.AddSessionEnd(new Point(100, 60));
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

        private void OnAddCardExecutor(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddCardExecutor(new Point(60, 60)); StatusText.Text = "Added Draw Card node (unsaved)."; }
        private void OnAddVariableCheck(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddVariableCheck(new Point(80, 60)); StatusText.Text = "Added Check node (unsaved)."; }
        private void OnAddActionNode(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddActionNode(new Point(100, 60)); StatusText.Text = "Added Action node (unsaved)."; }
        private void OnAddPhaseDecision(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddPhaseDecision(new Point(120, 60)); StatusText.Text = "Added Decision node (unsaved)."; }
        private void OnAddReturn(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddReturn(new Point(140, 60)); StatusText.Text = "Added Return node (unsaved)."; }
    }
}