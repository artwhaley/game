using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Graph Workbench shell (tickets 12 + 13): four horizontal panes — Library
    /// (18*), Session Graph (32*), Phase Graph (32*), Inspector (18*) — with
    /// three draggable splitters. Both graph panes host real Nodify editors
    /// (pan/zoom/select/move/connect) wired to canonical content view models.
    ///
    /// Ticket 13 turns the Session pane into a real authoring surface: sessions
    /// can be created/deleted/renamed, Start/PhaseReference/Decision/End nodes
    /// are added through SQLite (persisted immediately with output sockets),
    /// edges persist on connect/disconnect, node positions and viewport state
    /// persist in the wpf_* layout tables, and a Phase dragged from the Library
    /// onto the Session canvas becomes a PhaseReference placement.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly WorkbenchViewModel _vm;
        private bool _loaded;
        private bool _syncingSessionMeta;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new WorkbenchViewModel();
            DataContext = _vm;

            NodeDoubleClickCommand = new DelegateCommand<GraphNodeViewModel>(OnNodeDoubleClicked);

            // Inspector pane follows whichever editor has a selection.
            _vm.SessionGraph.SelectionChanged += (_, _) => UpdateInspector(_vm.SessionGraph.SelectedNode);
            _vm.PhaseGraph.SelectionChanged += (_, _) => UpdateInspector(_vm.PhaseGraph.SelectedNode);

            WireAuthoringEvents();
        }

        /// <summary>Double-click on a PhaseReference node opens that phase below.</summary>
        public ICommand NodeDoubleClickCommand { get; }

        // ---------- authoring persistence wiring ----------

        private void WireAuthoringEvents()
        {
            _vm.SessionGraph.NodeMoved += node => WithConnection(connection =>
            {
                if (_vm.SelectedSession == null) return;
                AuthoringLayoutRepository.SaveSessionNodePosition(
                    connection, _vm.SelectedSession.Id, node.Id, node.Location.X, node.Location.Y);
            });

            _vm.SessionGraph.ConnectionCreated += (source, target) => WithConnection(connection =>
            {
                if (_vm.SelectedSession == null || source?.Owner == null || target?.Owner == null) return;

                // One outgoing edge per output socket (schema UNIQUE). If the
                // source was already wired in the editor, drop the old edge first.
                if (source.IsConnected)
                {
                    SessionGraphRepository.RemoveEdgesFromSource(connection, source.Id);
                }

                var edge = new GraphEdgeDefinition
                {
                    Id = "se-" + Guid.NewGuid().ToString("N"),
                    SourceOutputId = source.Id,
                    TargetNodeId = target.Owner.Id,
                };
                SessionGraphRepository.AddEdge(connection, _vm.SelectedSession.Id, edge);
            });

            _vm.SessionGraph.ConnectionRemoved += connection => WithConnection(conn =>
            {
                if (connection?.Source == null) return;
                SessionGraphRepository.RemoveEdgesFromSource(conn, connection.Source.Id);
            });

            _vm.SessionGraph.NodeDeleted += node => WithConnection(connection =>
            {
                if (_vm.SelectedSession == null) return;
                SessionGraphRepository.RemoveNode(connection, _vm.SelectedSession.Id, node.Id);
                AuthoringLayoutRepository.DeleteSessionNodePosition(connection, _vm.SelectedSession.Id, node.Id);
            });

            _vm.SessionGraph.ViewportChanged += () => WithConnection(connection =>
            {
                if (_vm.SelectedSession == null) return;
                AuthoringLayoutRepository.SaveViewport(connection, "session", _vm.SelectedSession.Id,
                    _vm.SessionGraph.ViewportZoom, _vm.SessionGraph.ViewportLocation.X, _vm.SessionGraph.ViewportLocation.Y);
            });
        }

        private void OnNodeDoubleClicked(GraphNodeViewModel node)
        {
            if (node == null) return;
            if (node.Kind == "phase-reference" && !string.IsNullOrEmpty(node.RefId))
            {
                if (_vm.SelectPhaseById(node.RefId))
                {
                    SyncPhaseListSelection(node.RefId);
                    ReloadPhaseEditor();
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
                SessionMetaPanel.Visibility = Visibility.Collapsed;
                return;
            }
            InspTitle.Text = node.Title;
            InspId.Text = string.IsNullOrEmpty(node.RefId)
                ? "id: " + node.Id
                : "id: " + node.Id + "\nref: " + node.RefId;
            InspKind.Text = node.Kind + (string.IsNullOrEmpty(node.Subtitle) ? "" : "\n" + node.Subtitle);

            // Selecting the Start node exposes the Session metadata (title + type).
            SessionMetaPanel.Visibility = node.Kind == "start" ? Visibility.Visible : Visibility.Collapsed;
            if (node.Kind == "start")
            {
                SyncSessionMetaPanel();
            }
        }

        private void SyncSessionMetaPanel()
        {
            if (_vm.SelectedSession == null) return;
            _syncingSessionMeta = true;
            try
            {
                SessionTitleBox.Text = _vm.SelectedSession.Title;
                var types = _vm.Content?.SessionTypes;
                SessionTypeBox.ItemsSource = types;
                if (types != null)
                {
                    SessionTypeBox.SelectedItem = types.FirstOrDefault(t => t.Id == _vm.SelectedSession.SessionTypeId);
                }
            }
            finally
            {
                _syncingSessionMeta = false;
            }
        }

        private void OnSessionTitleChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingSessionMeta || _vm.SelectedSession == null) return;
            var title = SessionTitleBox.Text;
            WithConnection(connection => SessionRepository.Rename(connection, _vm.SelectedSession.Id, title));
            _vm.SelectedSession.Title = title;
            SessionHeader.Text = "Session Graph — " + title;
            BindSessionList();
        }

        private void OnSessionTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSessionMeta || _vm.SelectedSession == null) return;
            if (SessionTypeBox.SelectedItem is SessionTypeDefinition type)
            {
                WithConnection(connection => SessionRepository.SetSessionType(connection, _vm.SelectedSession.Id, type.Id));
                _vm.SelectedSession.SessionTypeId = type.Id;
                StatusText.Text = "Session type: " + type.Title;
            }
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
            BindSessionList();
            PhaseList.ItemsSource = _vm.Content.Phases;
            PhaseList.DisplayMemberPath = nameof(PhaseDefinition.Title);
            if (PhaseList.Items.Count > 0) PhaseList.SelectedIndex = 0;
        }

        private void BindSessionList()
        {
            var filter = (SessionFilter.Text ?? "").Trim();
            var sessions = _vm.Content.Sessions;
            var shown = string.IsNullOrEmpty(filter)
                ? sessions
                : sessions.Where(s => s.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            SessionList.ItemsSource = shown;
            SessionList.DisplayMemberPath = nameof(SessionDefinition.Title);
        }

        private void OnSessionFilterChanged(object sender, TextChangedEventArgs e)
        {
            BindSessionList();
        }

        private void OnSessionListChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SessionList.SelectedItem is SessionDefinition session)
            {
                _vm.SelectSession(session);
                SessionHeader.Text = "Session Graph — " + session.Title;
                ReloadSessionEditor();
            }
        }

        private void OnPhaseListChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PhaseList.SelectedItem is PhaseDefinition phase)
            {
                _vm.SelectPhase(phase);
                PhaseHeader.Text = "Phase Graph — " + phase.Title;
                ReloadPhaseEditor();
            }
        }

        /// <summary>
        /// Re-reads the selected session's graph + layout + viewport from SQLite
        /// and repopulates the editor. Called after every structural authoring
        /// edit so the canvas always mirrors the database.
        /// </summary>
        private void ReloadSessionEditor()
        {
            if (_vm.SelectedSession == null) return;
            WithConnection(connection =>
            {
                var graph = GameContentSnapshotLoader.LoadSessionGraph(connection, _vm.SelectedSession.Id);
                var layout = AuthoringLayoutRepository.LoadSessionNodePositions(connection, _vm.SelectedSession.Id);
                var viewport = AuthoringLayoutRepository.LoadViewport(connection, "session", _vm.SelectedSession.Id);
                _vm.SelectedSession.Graph = graph;
                _vm.SessionGraph.LoadFromDefinition(_vm.SelectedSession, layout, viewport);
            });
        }

        private void ReloadPhaseEditor()
        {
            if (_vm.SelectedPhase == null) return;
            WithConnection(connection =>
            {
                var layout = AuthoringLayoutRepository.LoadPhaseNodePositions(connection, _vm.SelectedPhase.Id);
                var viewport = AuthoringLayoutRepository.LoadViewport(connection, "phase", _vm.SelectedPhase.Id);
                _vm.PhaseGraph.LoadFromDefinition(_vm.SelectedPhase, layout, viewport);
            });
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
                    ReloadPhaseEditor();
                }
            };
            player.Show();
        }

        // ---------- session library CRUD ----------

        private void OnNewSession(object sender, RoutedEventArgs e)
        {
            var session = new SessionDefinition
            {
                Id = "session-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Title = "New Session",
                SessionTypeId = FirstSessionTypeId(),
            };
            WithConnection(connection => SessionRepository.Create(connection, session.Id, session.Title, session.SessionTypeId));
            _vm.Content.Sessions.Add(session);
            BindSessionList();
            SessionList.SelectedItem = session;
            StatusText.Text = "Created session '" + session.Title + "' — add a Start node to begin.";
        }

        private string FirstSessionTypeId()
        {
            if (_vm.Content != null && _vm.Content.SessionTypes.Count > 0)
            {
                return _vm.Content.SessionTypes[0].Id;
            }
            return "type-standard";
        }

        private void OnDeleteSession(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession == null) return;
            var session = _vm.SelectedSession;
            var confirm = MessageBox.Show(this,
                "Delete session '" + session.Title + "'? Its graph, layout and all authored nodes are removed.",
                "Delete Session", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            WithConnection(connection => SessionRepository.Delete(connection, session.Id));
            _vm.Content.Sessions.Remove(session);
            BindSessionList();
            if (_vm.Content.Sessions.Count > 0) SessionList.SelectedIndex = 0;
            else _vm.SelectSession(null);
            StatusText.Text = "Deleted session '" + session.Title + "'.";
        }

        private void OnCopySession(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Copy Session arrives with Ticket 17 (reuse/copy/make-unique).";
        }

        // ---------- palette actions (persisted authoring) ----------

        private void OnAddSessionStart(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession == null) { StatusText.Text = "Select a session first."; return; }
            if (_vm.SessionHasStart())
            {
                StatusText.Text = "Session already has a Start node (singular).";
                return;
            }
            var id = "sn-" + _vm.SelectedSession.Id + "-start";
            var node = new SessionStartNodeDefinition { Id = id };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            PersistSessionNode(node);
            StatusText.Text = "Added Start node.";
        }

        private void OnAddPhaseReference(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession == null) { StatusText.Text = "Select a session first."; return; }
            var phase = PhaseList.SelectedItem as PhaseDefinition ?? _vm.SelectedPhase;
            if (phase == null) { StatusText.Text = "Select a phase in the Library first."; return; }
            AddPhaseReference(phase, new Point(80, 60));
        }

        private void OnAddSessionDecision(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession == null) { StatusText.Text = "Select a session first."; return; }
            var id = "sn-" + _vm.SelectedSession.Id + "-decision-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var node = new SessionDecisionNodeDefinition { Id = id, Prompt = "Choose…" };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            PersistSessionNode(node);
            StatusText.Text = "Added Session Decision node (options arrive with Ticket 16).";
        }

        private void OnAddSessionEnd(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession == null) { StatusText.Text = "Select a session first."; return; }
            var id = "sn-" + _vm.SelectedSession.Id + "-end-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            PersistSessionNode(new SessionEndNodeDefinition { Id = id });
            StatusText.Text = "Added End node.";
        }

        /// <summary>
        /// Builds a PhaseReference node with one projected phase_exit output
        /// socket per exit of the referenced phase (live projection), then
        /// persists it.
        /// </summary>
        private void AddPhaseReference(PhaseDefinition phase, Point location)
        {
            var id = "sn-" + _vm.SelectedSession.Id + "-ref-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var node = new PhaseReferenceNodeDefinition { Id = id, PhaseId = phase.Id };
            foreach (var exit in phase.Exits)
            {
                node.Outputs.Add(new GraphOutputDefinition
                {
                    Id = id + "-exit-" + exit.Id,
                    Kind = GraphPortKind.PhaseExit,
                    PhaseExitId = exit.Id,
                });
            }
            PersistSessionNode(node, location);
            StatusText.Text = "Placed Phase: " + phase.Title;
        }

        private void PersistSessionNode(SessionGraphNodeDefinition node, Point? location = null)
        {
            var point = location ?? NextAutoPosition();
            WithConnection(connection =>
            {
                SessionGraphRepository.AddNode(connection, _vm.SelectedSession.Id, node);
                AuthoringLayoutRepository.SaveSessionNodePosition(connection, _vm.SelectedSession.Id, node.Id, point.X, point.Y);
            });
            ReloadSessionEditor();
        }

        private Point NextAutoPosition()
        {
            var offset = 60.0 + _vm.SessionGraph.Nodes.Count * 36;
            return new Point(offset, offset);
        }

        private void OnDeleteSelectedNode(object sender, RoutedEventArgs e)
        {
            DeleteSelectedNode(_vm.SessionGraph.SelectedNode);
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                var editor = sender as System.Windows.Controls.Control;
                var isSession = ReferenceEquals(editor, SessionEditor);
                var node = isSession ? _vm.SessionGraph.SelectedNode : _vm.PhaseGraph.SelectedNode;
                DeleteSelectedNode(node);
                e.Handled = true;
            }
        }

        private void DeleteSelectedNode(GraphNodeViewModel node)
        {
            if (node == null) return;
            if (_vm.SessionGraph.Nodes.Contains(node))
            {
                _vm.SessionGraph.DeleteNode(node);
                StatusText.Text = "Deleted node " + node.Id + ".";
            }
        }

        // ---------- phase palette (unsaved placeholders; Ticket 14 persists them) ----------

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
            StatusText.Text = "Added Entry node (phase persistence arrives with Ticket 14).";
        }

        private void OnAddCardExecutor(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddCardExecutor(new Point(80, 60)); StatusText.Text = "Added Draw Card node (phase persistence arrives with Ticket 14)."; }
        private void OnAddVariableCheck(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddVariableCheck(new Point(120, 60)); StatusText.Text = "Added Check node (phase persistence arrives with Ticket 14)."; }
        private void OnAddActionNode(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddActionNode(new Point(160, 60)); StatusText.Text = "Added Action node (phase persistence arrives with Ticket 14)."; }
        private void OnAddPhaseDecision(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddPhaseDecision(new Point(200, 60)); StatusText.Text = "Added Decision node (phase persistence arrives with Ticket 14)."; }
        private void OnAddReturn(object sender, RoutedEventArgs e) { _vm.PhaseGraph.AddReturn(new Point(240, 60)); StatusText.Text = "Added Return node (phase persistence arrives with Ticket 14)."; }

        // ---------- drag a Phase from the Library onto the Session canvas ----------

        private void OnSessionListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (!(SessionList.SelectedItem is PhaseDefinition phase)) return;
            var data = new DataObject("TruthCardGame.PhaseId", phase.Id);
            DragDrop.DoDragDrop(SessionList, data, DragDropEffects.Copy);
        }

        private void OnSessionDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("TruthCardGame.PhaseId") ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnSessionDrop(object sender, DragEventArgs e)
        {
            if (_vm.SelectedSession == null) { StatusText.Text = "Select a session first."; return; }
            if (!e.Data.GetDataPresent("TruthCardGame.PhaseId")) return;
            var phaseId = (string)e.Data.GetData("TruthCardGame.PhaseId");
            var phase = _vm.Content?.Phases.FirstOrDefault(p => p.Id == phaseId);
            if (phase == null) { StatusText.Text = "Dropped phase not found: " + phaseId; return; }

            // Convert the drop point (editor coordinates) to graph coordinates
            // by undoing the viewport pan/zoom transform.
            var drop = e.GetPosition(SessionEditor);
            var zoom = _vm.SessionGraph.ViewportZoom;
            var location = _vm.SessionGraph.ViewportLocation;
            var graphPoint = zoom > 0.0001
                ? new Point((drop.X - location.X) / zoom, (drop.Y - location.Y) / zoom)
                : drop;

            AddPhaseReference(phase, graphPoint);
        }

        // ---------- DB helper ----------

        private void WithConnection(Action<SqliteConnection> action)
        {
            try
            {
                var path = ReferencePlayerWindow.ResolveDatabasePath();
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    connection.Open();
                    ConnectionInitializer.Initialize(connection);
                    action(connection);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Persistence error";
                MessageBox.Show(this, "Database write failed:\n\n" + ex.Message,
                    "Workbench", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
