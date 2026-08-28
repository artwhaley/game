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
    /// <summary>Collapses when the bound value is null (inline check editors).</summary>
    public sealed class NullToCollapsedConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Collapses when the bound bool is false (inline GOTO rows).</summary>
    public sealed class BoolToVisibleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool flag && flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public partial class MainWindow : Window
    {
        private readonly WorkbenchViewModel _vm;
        private readonly AuthoringCommandStack _stack = new AuthoringCommandStack();
        private bool _loaded;
        private bool _syncingSessionMeta;
        private bool _syncingPhaseMeta;
        private bool _loadingPlacementPhase;
        private bool _suppressDisconnectCommands;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new WorkbenchViewModel();
            DataContext = _vm;

            NodeDoubleClickCommand = new DelegateCommand<GraphNodeViewModel>(OnNodeDoubleClicked);

            // Ticket 18: semantic undo/redo. Every authoring edit is pushed as an
            // IAuthoringCommand; Ctrl+Z / Ctrl+Y and the toolbar buttons drive it.
            UndoCommand = new DelegateCommand<object>(_ => Undo(), _ => _stack.CanUndo);
            RedoCommand = new DelegateCommand<object>(_ => Redo(), _ => _stack.CanRedo);
            _stack.Changed += () =>
            {
                UndoCommand.RaiseCanExecuteChanged();
                RedoCommand.RaiseCanExecuteChanged();
            };
            PreviewKeyDown += OnWindowPreviewKeyDown;

            // Inspector pane follows whichever editor has a selection.
            _vm.SessionGraph.SelectionChanged += (_, _) => UpdateInspector(_vm.SessionGraph.SelectedNode, fromSession: true);
            _vm.PhaseGraph.SelectionChanged += (_, _) => UpdateInspector(_vm.PhaseGraph.SelectedNode, fromSession: false);

            WireAuthoringEvents();
        }

        /// <summary>Double-click on a PhaseReference node opens that phase below.</summary>
        public ICommand NodeDoubleClickCommand { get; }

        /// <summary>Ctrl+Z undo (Ticket 18).</summary>
        public DelegateCommand<object> UndoCommand { get; }

        /// <summary>Ctrl+Y redo (Ticket 18).</summary>
        public DelegateCommand<object> RedoCommand { get; }

        // ---------- authoring persistence wiring ----------

        private void WireAuthoringEvents()
        {
            // ---- session editor ----

            _vm.SessionGraph.NodeMoved += node =>
            {
                if (_vm.SelectedSession == null) return;
                var original = WithConnectionResult(connection =>
                    AuthoringLayoutRepository.GetSessionNodePosition(connection, _vm.SelectedSession.Id, node.Id));
                PushOrMerge(new MoveNodeCommand(OpenConnection, "session", _vm.SelectedSession.Id, node.Id,
                    new Point2(original?.X ?? node.Location.X, original?.Y ?? node.Location.Y),
                    new Point2(node.Location.X, node.Location.Y)));
            };

            _vm.SessionGraph.ConnectionCreated += (source, target) =>
            {
                if (_vm.SelectedSession == null || source?.Owner == null || target?.Owner == null) return;
                var replaced = WithConnectionResult(connection =>
                {
                    var edges = AuthoringUndo.EdgesFromSource(connection, source.Id);
                    return edges.Count > 0 ? edges[0].TargetNodeId : null;
                });
                PushCommand(new ConnectSessionCommand(OpenConnection, _vm.SelectedSession.Id,
                    source.Id, target.Owner.Id, replaced));
            };

            _vm.SessionGraph.ConnectionRemoved += connection =>
            {
                if (_suppressDisconnectCommands) return;
                if (_vm.SelectedSession == null || connection?.Source?.Owner == null || connection?.Target?.Owner == null) return;
                PushCommand(new DisconnectSessionCommand(OpenConnection, _vm.SelectedSession.Id,
                    connection.Source.Id, connection.Target.Owner.Id));
            };

            _vm.SessionGraph.NodeDeleted += node =>
            {
                if (_vm.SelectedSession == null) return;
                var snapshot = CaptureSessionNodeSnapshot(node);
                if (snapshot == null) return;
                PushCommand(snapshot, reloadSession: false);
            };

            _vm.SessionGraph.ViewportChanged += () => WithConnection(connection =>
            {
                if (_vm.SelectedSession == null) return;
                AuthoringLayoutRepository.SaveViewport(connection, "session", _vm.SelectedSession.Id,
                    _vm.SessionGraph.ViewportZoom, _vm.SessionGraph.ViewportLocation.X, _vm.SessionGraph.ViewportLocation.Y);
            });

            // ---- phase editor ----

            _vm.PhaseGraph.NodeMoved += node =>
            {
                if (_vm.SelectedPhase == null) return;
                var original = WithConnectionResult(connection =>
                    AuthoringLayoutRepository.GetPhaseNodePosition(connection, _vm.SelectedPhase.Id, node.Id));
                PushOrMerge(new MoveNodeCommand(OpenConnection, "phase", _vm.SelectedPhase.Id, node.Id,
                    new Point2(original?.X ?? node.Location.X, original?.Y ?? node.Location.Y),
                    new Point2(node.Location.X, node.Location.Y)));
            };

            _vm.PhaseGraph.ConnectionCreated += (source, target) =>
            {
                if (_vm.SelectedPhase == null || source?.Owner == null || target?.Owner == null) return;
                var replaced = WithConnectionResult(connection =>
                {
                    var edges = AuthoringUndo.EdgesFromSource(connection, source.Id);
                    return edges.Count > 0 ? edges[0].TargetNodeId : null;
                });
                PushCommand(new ConnectPhaseCommand(OpenConnection, _vm.SelectedPhase.Id,
                    source.Id, target.Owner.Id, replaced));
            };

            _vm.PhaseGraph.ConnectionRemoved += connection =>
            {
                if (_suppressDisconnectCommands) return;
                if (_vm.SelectedPhase == null || connection?.Source?.Owner == null || connection?.Target?.Owner == null) return;
                PushCommand(new DisconnectPhaseCommand(OpenConnection, _vm.SelectedPhase.Id,
                    connection.Source.Id, connection.Target.Owner.Id));
            };

            _vm.PhaseGraph.NodeDeleted += node =>
            {
                if (_vm.SelectedPhase == null) return;
                var snapshot = CapturePhaseNodeSnapshot(node);
                if (snapshot == null) return;
                PushCommand(snapshot, reloadPhase: false);
            };

            _vm.PhaseGraph.CheckChanged += (node, field) =>
            {
                if (_vm.SelectedPhase == null || node?.Check == null) return;
                var current = WithConnectionResult(connection => AuthoringUndo.GetVariableCheck(connection, node.Id));
                var check = node.Check;
                var key = check.Source == "progress" ? "" : check.Key;
                PushOrMerge(new UpdateCheckCommand(OpenConnection, node.Id, field,
                    current.Source, current.Key, current.Op, current.Value,
                    check.Source, key, check.Operator, ParseFloat(check.ValueText)));
            };

            _vm.PhaseGraph.ViewportChanged += () => WithConnection(connection =>
            {
                if (_vm.SelectedPhase == null) return;
                AuthoringLayoutRepository.SaveViewport(connection, "phase", _vm.SelectedPhase.Id,
                    _vm.PhaseGraph.ViewportZoom, _vm.PhaseGraph.ViewportLocation.X, _vm.PhaseGraph.ViewportLocation.Y);
            });

            _vm.PhaseGraph.GotoExitChanged += (node, row, field) =>
            {
                if (row == null || string.IsNullOrEmpty(row.InstanceId)) return;
                if (row.Scope == "session")
                {
                    if (field != nameof(GotoRowData.Label)) return;
                    var oldLabel = WithConnectionResult(connection =>
                        AuthoringUndo.GetSessionGotoLabel(connection, row.InstanceId));
                    PushOrMerge(new SetSessionGotoLabelCommand(OpenConnection, row.InstanceId, oldLabel, row.Label ?? ""));
                }
                else
                {
                    if (field != nameof(GotoRowData.ExitId) || string.IsNullOrEmpty(row.ExitId)) return;
                    var oldExit = WithConnectionResult(connection =>
                        AuthoringUndo.GetPhaseGotoExit(connection, row.InstanceId));
                    PushCommand(new SetPhaseGotoExitCommand(OpenConnection, row.InstanceId, oldExit, row.ExitId));
                }
            };

            _vm.PhaseGraph.DecisionPromptChanged += node =>
            {
                if (node?.DecisionScope == null) return;
                var oldPrompt = WithConnectionResult(connection =>
                    AuthoringUndo.GetDecisionPrompt(connection, node.DecisionScope, node.Id));
                PushOrMerge(new SetDecisionPromptCommand(OpenConnection, node.DecisionScope, node.Id,
                    oldPrompt, node.DecisionPrompt ?? ""));
            };

            _vm.PhaseGraph.DecisionRowChanged += (node, option) =>
            {
                if (option?.Scope == null || string.IsNullOrEmpty(option.OptionId)) return;
                var oldLabel = WithConnectionResult(connection =>
                    AuthoringUndo.GetOptionLabel(connection, option.Scope, option.OptionId));
                PushOrMerge(new RenameOptionCommand(OpenConnection, option.Scope, option.OptionId,
                    oldLabel, option.Label ?? ""));
            };

            _vm.SessionGraph.DecisionPromptChanged += node =>
            {
                if (node?.DecisionScope != "session") return;
                var oldPrompt = WithConnectionResult(connection =>
                    AuthoringUndo.GetDecisionPrompt(connection, "session", node.Id));
                PushOrMerge(new SetDecisionPromptCommand(OpenConnection, "session", node.Id,
                    oldPrompt, node.DecisionPrompt ?? ""));
            };

            _vm.SessionGraph.DecisionRowChanged += (node, option) =>
            {
                if (option?.Scope != "session" || string.IsNullOrEmpty(option.OptionId)) return;
                var oldLabel = WithConnectionResult(connection =>
                    AuthoringUndo.GetOptionLabel(connection, "session", option.OptionId));
                PushOrMerge(new RenameOptionCommand(OpenConnection, "session", option.OptionId,
                    oldLabel, option.Label ?? ""));
            };

            _vm.SessionGraph.GotoExitChanged += (node, row, field) =>
            {
                if (row == null || string.IsNullOrEmpty(row.InstanceId) || row.Scope != "session") return;
                if (field != nameof(GotoRowData.Label)) return;
                var oldLabel = WithConnectionResult(connection =>
                    AuthoringUndo.GetSessionGotoLabel(connection, row.InstanceId));
                PushOrMerge(new SetSessionGotoLabelCommand(OpenConnection, row.InstanceId, oldLabel, row.Label ?? ""));
            };
        }

        // ---------- undo/redo (ticket 18) ----------

        /// <summary>Opens one initialized connection per command execution.</summary>
        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection("Data Source=" + ReferencePlayerWindow.ResolveDatabasePath());
            connection.Open();
            ConnectionInitializer.Initialize(connection);
            return connection;
        }

        private void PushCommand(IAuthoringCommand command, bool reloadSession = false, bool reloadPhase = false)
        {
            try
            {
                _stack.PushOrMerge(command);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Persistence error";
                MessageBox.Show(this, "Database write failed:\n\n" + ex.Message,
                    "Workbench", MessageBoxButton.OK, MessageBoxImage.Error);
                ReloadAllFromDb();
                return;
            }
            if (reloadSession) ReloadSessionEditor();
            if (reloadPhase) ReloadPhaseEditor();
        }

        private void PushOrMerge(IAuthoringCommand command)
        {
            try
            {
                _stack.PushOrMerge(command);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Persistence error";
                MessageBox.Show(this, "Database write failed:\n\n" + ex.Message,
                    "Workbench", MessageBoxButton.OK, MessageBoxImage.Error);
                ReloadAllFromDb();
            }
        }

        private void Undo()
        {
            if (!_stack.CanUndo) return;
            try
            {
                _stack.Undo();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Undo failed:\n\n" + ex.Message,
                    "Undo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ReloadAllFromDb();
        }

        private void Redo()
        {
            if (!_stack.CanRedo) return;
            try
            {
                _stack.Redo();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Redo failed:\n\n" + ex.Message,
                    "Redo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ReloadAllFromDb();
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                Undo();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
            {
                Redo();
                e.Handled = true;
            }
        }

        /// <summary>
        /// After an undo/redo the database is the only trustworthy state: reload
        /// the whole snapshot, re-select the previously visible session/phase,
        /// and repopulate both canvases.
        /// </summary>
        private void ReloadAllFromDb()
        {
            var sessionId = _vm.SelectedSession?.Id;
            var phaseId = _vm.SelectedPhase?.Id;
            var wasPlacementContext = _vm.PhaseGraph.HasPlacementContext;

            LoadContent();

            if (sessionId != null)
            {
                var session = _vm.Content.Sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                {
                    _vm.SelectSession(session);
                    SyncSessionListSelection(session);
                }
            }
            if (phaseId != null)
            {
                var phase = _vm.Content.Phases.FirstOrDefault(p => p.Id == phaseId);
                if (phase != null)
                {
                    _vm.SelectPhase(phase);
                    _vm.PhaseGraph.HasPlacementContext = wasPlacementContext;
                    SyncPhaseListSelection(phaseId);
                    UpdatePhaseHeader();
                }
            }
            ReloadSessionEditor();
            ReloadPhaseEditor();
            UpdatePhaseHeader();
        }

        private void SyncSessionListSelection(SessionDefinition session)
        {
            SessionList.SelectionChanged -= OnSessionListChanged;
            try
            {
                SessionList.SelectedItem = session;
                SessionList.ScrollIntoView(session);
            }
            finally
            {
                SessionList.SelectionChanged += OnSessionListChanged;
            }
        }

        private DeleteSessionNodeCommand CaptureSessionNodeSnapshot(GraphNodeViewModel node)
        {
            if (_vm.SelectedSession == null) return null;
            var definition = _vm.SelectedSession.Graph?.Nodes.Find(n => n.Id == node.Id);
            if (definition == null) return null;
            var (edges, position) = WithConnectionResult(connection =>
            {
                var touching = AuthoringUndo.SessionEdgesTouchingNode(connection, _vm.SelectedSession.Id, node.Id);
                var pos = AuthoringLayoutRepository.GetSessionNodePosition(connection, _vm.SelectedSession.Id, node.Id);
                return (touching, pos.HasValue ? new Point2(pos.Value.X, pos.Value.Y) : (Point2?)null);
            });
            return new DeleteSessionNodeCommand(OpenConnection, _vm.SelectedSession.Id, definition, edges, position);
        }

        private DeletePhaseNodeCommand CapturePhaseNodeSnapshot(GraphNodeViewModel node)
        {
            if (_vm.SelectedPhase == null) return null;
            var definition = _vm.SelectedPhase.Graph?.Nodes.Find(n => n.Id == node.Id) as PhaseGraphNodeDefinition;
            if (definition == null) return null;
            var (edges, position) = WithConnectionResult(connection =>
            {
                var touching = AuthoringUndo.PhaseEdgesTouchingNode(connection, _vm.SelectedPhase.Id, node.Id);
                var pos = AuthoringLayoutRepository.GetPhaseNodePosition(connection, _vm.SelectedPhase.Id, node.Id);
                return (touching, pos.HasValue ? new Point2(pos.Value.X, pos.Value.Y) : (Point2?)null);
            });
            return new DeletePhaseNodeCommand(OpenConnection, _vm.SelectedPhase.Id, definition, edges, position);
        }

        private void OnAddGoto(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is GraphNodeViewModel node)) return;
            if (_vm.SelectedPhase == null || _vm.SelectedPhase.Exits.Count == 0)
            {
                StatusText.Text = "Add an exit to this phase first (exits strip above).";
                return;
            }
            PushCommand(new AddPhaseGotoCommand(OpenConnection, node.Id, _vm.SelectedPhase.Exits[0].Id),
                reloadPhase: true);
            StatusText.Text = "Added PhaseGoto instance (blocking).";
        }

        private void OnRemoveGoto(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is GotoRowData row)) return;
            if (string.IsNullOrEmpty(row.InstanceId)) return;
            var snapshot = WithConnectionResult(connection => AuthoringUndo.SnapshotPhaseGoto(connection, row.InstanceId));
            PushCommand(new RemovePhaseGotoCommand(OpenConnection, snapshot), reloadPhase: true);
            StatusText.Text = "Removed PhaseGoto instance.";
        }

        private void OnAddDecisionOption(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is GraphNodeViewModel node)) return;
            if (string.IsNullOrEmpty(node.DecisionScope)) return;
            var optionId = node.Id + "-opt-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var sequenceId = optionId + "-seq";
            PushCommand(new AddDecisionOptionCommand(OpenConnection, node.DecisionScope, node.Id,
                optionId, "Option", sequenceId), reloadSession: true, reloadPhase: true);
            StatusText.Text = "Added decision option.";
        }

        private void OnRemoveDecisionOption(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is DecisionRowData option)) return;
            if (string.IsNullOrEmpty(option.OptionId)) return;
            object snapshot = option.Scope == "session"
                ? (object)WithConnectionResult(connection =>
                    AuthoringUndo.SnapshotSessionOption(connection, option.NodeId, option.OptionId))
                : WithConnectionResult(connection =>
                    AuthoringUndo.SnapshotPhaseOption(connection, option.NodeId, option.OptionId));
            PushCommand(new RemoveDecisionOptionCommand(OpenConnection, option.Scope, snapshot),
                reloadSession: true, reloadPhase: true);
            StatusText.Text = "Removed decision option.";
        }

        private void OnAddDecisionGoto(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is DecisionRowData option)) return;
            if (option.Scope == "session")
            {
                if (_vm.SelectedSession == null) return;
                PushCommand(new AddSessionGotoCommand(OpenConnection, option.NodeId, option.OptionId, "goto"),
                    reloadSession: true);
                StatusText.Text = "Added SessionGoto — a unique port appeared on the decision node.";
            }
            else
            {
                if (_vm.SelectedPhase == null || _vm.SelectedPhase.Exits.Count == 0)
                {
                    StatusText.Text = "Add an exit to this phase first (exits strip above).";
                    return;
                }
                PushCommand(new AddPhaseOptionGotoCommand(OpenConnection, option.OptionId, _vm.SelectedPhase.Exits[0].Id),
                    reloadPhase: true);
                StatusText.Text = "Added PhaseGoto to option sequence.";
            }
        }

        private void OnRemoveDecisionGoto(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is GotoRowData row)) return;
            if (string.IsNullOrEmpty(row.InstanceId)) return;
            if (row.Scope == "session")
            {
                var snapshot = WithConnectionResult(connection =>
                    AuthoringUndo.SnapshotSessionGoto(connection, row.InstanceId));
                PushCommand(new RemoveSessionGotoCommand(OpenConnection, snapshot), reloadSession: true);
            }
            else
            {
                var snapshot = WithConnectionResult(connection =>
                    AuthoringUndo.SnapshotPhaseGoto(connection, row.InstanceId));
                PushCommand(new RemovePhaseGotoCommand(OpenConnection, snapshot), reloadPhase: true);
            }
            StatusText.Text = "Removed GOTO (and its unique port, if any).";
        }

        private static VariableSourceKind ParseSourceKind(string source)
        {
            switch (source)
            {
                case "temperature": return VariableSourceKind.Temperature;
                case "stat": return VariableSourceKind.Stat;
                default: return VariableSourceKind.PhaseProgress;
            }
        }

        private static VariableCompareOperator ParseOperator(string op)
        {
            switch (op)
            {
                case "<": return VariableCompareOperator.LessThan;
                case "<=": return VariableCompareOperator.LessThanOrEqual;
                case "==": return VariableCompareOperator.Equal;
                case "!=": return VariableCompareOperator.NotEqual;
                case ">": return VariableCompareOperator.GreaterThan;
                default: return VariableCompareOperator.GreaterThanOrEqual;
            }
        }

        private static float ParseFloat(string text)
        {
            if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)) return value;
            return 0f;
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

        private void UpdateInspector(GraphNodeViewModel node, bool fromSession)
        {
            if (node == null)
            {
                // While a placement is loading the phase editor repopulates and
                // fires a null-selection event; don't blank the Inspector then.
                if (_loadingPlacementPhase) return;
                InspTitle.Text = "(none)";
                InspId.Text = "";
                InspKind.Text = "";
                SessionMetaPanel.Visibility = Visibility.Collapsed;
                PhaseMetaPanel.Visibility = Visibility.Collapsed;
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

            // Selecting the Entry node exposes the Phase metadata (title + tags).
            PhaseMetaPanel.Visibility = node.Kind == "entry" ? Visibility.Visible : Visibility.Collapsed;
            if (node.Kind == "entry")
            {
                SyncPhaseMetaPanel();
            }

            // Selecting a PhaseReference in the Session canvas loads that exact
            // Phase below and establishes placement context (Ticket 14).
            if (fromSession && node.Kind == "phase-reference" && !string.IsNullOrEmpty(node.RefId))
            {
                if (_loadingPlacementPhase) return;
                _loadingPlacementPhase = true;
                try
                {
                    if (_vm.SelectPhaseById(node.RefId))
                    {
                        _vm.PhaseGraph.HasPlacementContext = true;
                        SyncPhaseListSelection(node.RefId);
                        ReloadPhaseEditor();
                        UpdatePhaseHeader();
                    }
                }
                finally
                {
                    _loadingPlacementPhase = false;
                }
            }
        }

        private void SyncPhaseMetaPanel()
        {
            if (_vm.SelectedPhase == null) return;
            _syncingPhaseMeta = true;
            try
            {
                PhaseTitleBox.Text = _vm.SelectedPhase.Title;
                PhaseIncludeTagsBox.Text = string.Join(", ", _vm.SelectedPhase.MustIncludeTags);
                PhaseExcludeTagsBox.Text = string.Join(", ", _vm.SelectedPhase.MustExcludeTags);
            }
            finally
            {
                _syncingPhaseMeta = false;
            }
        }

        private void OnPhaseTitleChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingPhaseMeta || _vm.SelectedPhase == null) return;
            var oldTitle = _vm.SelectedPhase.Title;
            var newTitle = PhaseTitleBox.Text;
            PushOrMerge(new RenamePhaseCommand(OpenConnection, _vm.SelectedPhase.Id, oldTitle, newTitle));
            _vm.SelectedPhase.Title = newTitle;
            PhaseHeader.Text = "Phase Graph — " + newTitle;
            BindPhaseList();
        }

        private void OnPhaseTagsChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingPhaseMeta || _vm.SelectedPhase == null) return;
            var include = SplitTags(PhaseIncludeTagsBox.Text);
            var exclude = SplitTags(PhaseExcludeTagsBox.Text);
            WithConnection(connection =>
            {
                PhaseRepository.ReplaceRequiredTags(connection, _vm.SelectedPhase.Id, include);
                PhaseRepository.ReplaceExcludedTags(connection, _vm.SelectedPhase.Id, exclude);
            });
            _vm.SelectedPhase.MustIncludeTags = include;
            _vm.SelectedPhase.MustExcludeTags = exclude;
            StatusText.Text = "Phase tags updated.";
        }

        private static List<string> SplitTags(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var part in text.Split(','))
            {
                var tag = part.Trim();
                if (tag.Length > 0) result.Add(tag);
            }
            return result;
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
            var oldTitle = _vm.SelectedSession.Title;
            var newTitle = SessionTitleBox.Text;
            PushOrMerge(new RenameSessionCommand(OpenConnection, _vm.SelectedSession.Id, oldTitle, newTitle));
            _vm.SelectedSession.Title = newTitle;
            SessionHeader.Text = "Session Graph — " + newTitle;
            BindSessionList();
        }

        private void OnSessionTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSessionMeta || _vm.SelectedSession == null) return;
            if (SessionTypeBox.SelectedItem is SessionTypeDefinition type)
            {
                var oldType = _vm.SelectedSession.SessionTypeId;
                PushCommand(new SetSessionTypeCommand(OpenConnection, _vm.SelectedSession.Id, oldType, type.Id));
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
            BindPhaseList();
        }

        private void BindPhaseList()
        {
            var filter = (PhaseFilter.Text ?? "").Trim();
            var phases = _vm.Content.Phases;
            var shown = string.IsNullOrEmpty(filter)
                ? phases
                : phases.Where(p => p.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            PhaseList.ItemsSource = shown;
            PhaseList.DisplayMemberPath = nameof(PhaseDefinition.Title);
            if (PhaseList.Items.Count > 0 && PhaseList.SelectedItem == null) PhaseList.SelectedIndex = 0;
        }

        private void OnPhaseFilterChanged(object sender, TextChangedEventArgs e)
        {
            BindPhaseList();
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
                // Undo history is per-document: switching clears it (Ticket 18).
                _stack.Clear();
                _vm.SelectSession(session);
                SessionHeader.Text = "Session Graph — " + session.Title;
                ReloadSessionEditor();
            }
        }

        private void OnPhaseListChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PhaseList.SelectedItem is PhaseDefinition phase)
            {
                _stack.Clear();
                _vm.SelectPhase(phase);
                _vm.PhaseGraph.HasPlacementContext = false;
                PhaseHeader.Text = "Phase Graph — " + phase.Title;
                ReloadPhaseEditor();
                UpdatePhaseHeader();
            }
        }

        /// <summary>Refreshes the Phase header readout: name, usage, exits strip.</summary>
        private void UpdatePhaseHeader()
        {
            if (_vm.SelectedPhase == null)
            {
                PhaseUsageText.Text = "";
                ExitsStrip.ItemsSource = null;
                return;
            }
            var (sessions, placements) = WithConnectionResult(connection =>
                PhaseRepository.Usage(connection, _vm.SelectedPhase.Id));
            var placement = _vm.PhaseGraph.HasPlacementContext ? " · placed in session" : "";
            PhaseUsageText.Text = $"used in {sessions} session(s) · {placements} placement(s){placement}";

            // Exits strip: rename always; add/delete only while placement count <= 1.
            var canEditExits = placements <= 1;
            AddExitButton.IsEnabled = canEditExits;
            var rows = new List<ExitRowViewModel>();
            foreach (var exit in _vm.SelectedPhase.Exits)
            {
                var row = new ExitRowViewModel
                {
                    Id = exit.Id,
                    Name = exit.Name,
                    CanDelete = canEditExits,
                };
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName != nameof(ExitRowViewModel.Name)) return;
                    var def = _vm.SelectedPhase.Exits.Find(x => x.Id == row.Id);
                    var oldName = def?.Name ?? row.Name;
                    PushCommand(new RenameExitCommand(OpenConnection, row.Id, oldName, row.Name));
                    if (def != null) def.Name = row.Name;
                    // Live projection: rename updates the port label without breaking
                    // wiring (identity is the exit id); refresh both canvases.
                    ReloadPhaseEditor();
                    ReloadSessionEditor();
                    UpdatePhaseHeader();
                };
                rows.Add(row);
            }
            ExitsStrip.ItemsSource = rows;
        }

        private void OnAddExit(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) return;
            var (sessions, placements) = WithConnectionResult(connection =>
                PhaseRepository.Usage(connection, _vm.SelectedPhase.Id));
            if (placements > 1)
            {
                // Port topology lock: offer Make Unique, then apply the add on the clone.
                PromptSharedPortEdit(sessions, placements, newPhaseId =>
                {
                    var exitId = "px-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    return new CreateExitCommand(OpenConnection, newPhaseId,
                        new PhaseExitDefinition { Id = exitId, Name = "Exit " + (_vm.SelectedPhase.Exits.Count + 1) }, -1);
                });
                return;
            }

            var exitId = "px-" + _vm.SelectedPhase.Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var ordinal = _vm.SelectedPhase.Exits.Count;
            var def = new PhaseExitDefinition { Id = exitId, Name = "Exit " + (ordinal + 1) };
            PushCommand(new CreateExitCommand(OpenConnection, _vm.SelectedPhase.Id, def, ordinal));
            _vm.SelectedPhase.Exits.Add(def);
            UpdatePhaseHeader();
            ReloadSessionEditor();
            ReloadPhaseEditor();
            StatusText.Text = "Added exit — every placement's PhaseReference now projects it.";
        }

        private void OnDeleteExit(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem menu) || !(menu.DataContext is ExitRowViewModel row)) return;
            if (_vm.SelectedPhase == null) return;

            var (sessions, placements) = WithConnectionResult(connection =>
                PhaseRepository.Usage(connection, _vm.SelectedPhase.Id));
            if (placements > 1)
            {
                // Port topology lock: offer Make Unique, then apply the delete on the clone.
                var exitName = row.Name;
                PromptSharedPortEdit(sessions, placements, newPhaseId =>
                    new DeleteExitOnCloneCommand(OpenConnection, newPhaseId, exitName));
                return;
            }

            var confirm = MessageBox.Show(this,
                "Delete exit '" + row.Name + "'? If its sole projected output is wired, " +
                "the edge and output are removed as one transactional edit.",
                "Delete Exit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            // Transactional: schema ON DELETE CASCADE removes projected sockets and
            // their edges together. A GOTO Action that still references this exit
            // blocks the delete (RESTRICT) — we never silently auto-delete an exit.
            var projectedEdges = WithConnectionResult(connection =>
                AuthoringUndo.ExitProjectedEdges(connection, row.Id));
            var ordinal = _vm.SelectedPhase.Exits.FindIndex(x => x.Id == row.Id);
            var def = _vm.SelectedPhase.Exits.Find(x => x.Id == row.Id);
            PushCommand(new DeleteExitCommand(OpenConnection, _vm.SelectedPhase.Id, def, ordinal, projectedEdges));
            _vm.SelectedPhase.Exits.RemoveAll(x => x.Id == row.Id);
            UpdatePhaseHeader();
            ReloadSessionEditor();
            ReloadPhaseEditor();
            StatusText.Text = "Deleted exit '" + row.Name + "' (projected socket + edge removed).";
        }

        /// <summary>
        /// Shared-port topology lock popup (Ticket 17): changing a shared phase's
        /// ports could break existing session graphs. Offers Make Unique (which
        /// detaches the selected placement and applies the requested edit as ONE
        /// undoable command) or Cancel.
        /// </summary>
        private void PromptSharedPortEdit(int sessions, int placements, Func<string, IAuthoringCommand> exitEditFactory)
        {
            if (_vm.SessionGraph.SelectedNode?.Kind == "phase-reference")
            {
                var choice = MessageBox.Show(this,
                    "This Phase is used in " + sessions + " session(s) / " + placements + " placement(s).\n" +
                    "Changing its ports could break existing Session graphs.\n\n" +
                    "[Make Unique] detaches the selected placement so it can own this edit.\n" +
                    "[Cancel] leaves the shared phase untouched.",
                    "Shared Phase Port Lock", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (choice == MessageBoxResult.OK)
                {
                    MakeUniqueCurrentPlacement(exitEditFactory);
                    StatusText.Text = "Placement made unique — the port edit was applied on the clone.";
                }
                return;
            }

            MessageBox.Show(this,
                "This Phase is used in " + sessions + " session(s) / " + placements + " placement(s).\n" +
                "Select a PhaseReference placement in the Session canvas, then Make Unique " +
                "to detach it before editing ports.",
                "Shared Phase Port Lock", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (_vm.SelectedSession == null) return;
            var source = _vm.SelectedSession;
            var newId = "session-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var clone = ContentCloner.CloneSession(source, newId, source.Title + " (copy)");
            var layout = WithConnectionResult(connection =>
            {
                var remapped = new List<(string NodeId, double X, double Y)>();
                var saved = AuthoringLayoutRepository.LoadSessionNodePositions(connection, source.Id);
                foreach (var pair in saved)
                {
                    if (clone.NodeIdMap.TryGetValue(pair.Key, out var newNodeId))
                    {
                        remapped.Add((newNodeId, pair.Value.X, pair.Value.Y));
                    }
                }
                return remapped;
            });
            PushCommand(new CopySessionCommand(OpenConnection, clone.Session, layout));

            _vm.Content.Sessions.Add(clone.Session);
            BindSessionList();
            SessionList.SelectedItem = clone.Session;
            StatusText.Text = "Copied session (Phase references stay shared — reuse is reuse).";
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
            if (_vm.SessionGraph.SelectedNode != null) DeleteSelectedNode(_vm.SessionGraph.SelectedNode);
            else if (_vm.PhaseGraph.SelectedNode != null) DeleteSelectedNode(_vm.PhaseGraph.SelectedNode);
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
                // The editor raises ConnectionRemoved per touching edge; those are
                // part of the single DeleteNodeCommand, so suppress standalone
                // disconnect pushes while the delete event cascade runs.
                _suppressDisconnectCommands = true;
                try
                {
                    _vm.SessionGraph.DeleteNode(node);
                }
                finally
                {
                    _suppressDisconnectCommands = false;
                }
                StatusText.Text = "Deleted node " + node.Id + ".";
            }
            else if (_vm.PhaseGraph.Nodes.Contains(node))
            {
                if (node.Kind == "entry")
                {
                    StatusText.Text = "The Entry node cannot be deleted (singular).";
                    return;
                }
                _suppressDisconnectCommands = true;
                try
                {
                    _vm.PhaseGraph.DeleteNode(node);
                }
                finally
                {
                    _suppressDisconnectCommands = false;
                }
                StatusText.Text = "Deleted node " + node.Id + ".";
            }
        }

        // ---------- phase palette (persisted authoring, ticket 14) ----------

        private void OnAddPhaseEntry(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) { StatusText.Text = "Select a phase first."; return; }
            if (_vm.PhaseHasEntry())
            {
                StatusText.Text = "Phase already has an Entry node (singular).";
                return;
            }
            var id = "pn-" + _vm.SelectedPhase.Id + "-entry";
            var node = new PhaseEntryNodeDefinition { Id = id };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            PersistPhaseNode(node);
            StatusText.Text = "Added Entry node.";
        }

        private void OnAddCardExecutor(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) { StatusText.Text = "Select a phase first."; return; }
            var id = "pn-" + _vm.SelectedPhase.Id + "-card-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var node = new CardExecutorNodeDefinition { Id = id };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            PersistPhaseNode(node);
            StatusText.Text = "Added Draw Card node.";
        }

        private void OnAddVariableCheck(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) { StatusText.Text = "Select a phase first."; return; }
            var id = "pn-" + _vm.SelectedPhase.Id + "-check-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var node = new VariableCheckNodeDefinition
            {
                Id = id,
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 3f,
            };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-true", Kind = GraphPortKind.True });
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-false", Kind = GraphPortKind.False });
            PersistPhaseNode(node);
            StatusText.Text = "Added Check node — edit source/operator/literal inline.";
        }

        private void OnAddActionNode(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) { StatusText.Text = "Select a phase first."; return; }
            var id = "pn-" + _vm.SelectedPhase.Id + "-action-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var node = new ActionNodeDefinition
            {
                Id = id,
                Sequence = new ActionSequenceDefinition { Id = id + "-seq" },
            };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            PersistPhaseNode(node);
            StatusText.Text = "Added Action node (action UX arrives with Ticket 15/16).";
        }

        private void OnAddPhaseDecision(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) { StatusText.Text = "Select a phase first."; return; }
            var id = "pn-" + _vm.SelectedPhase.Id + "-decision-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var node = new PhaseDecisionNodeDefinition { Id = id, Prompt = "Choose…" };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            PersistPhaseNode(node);
            StatusText.Text = "Added Decision node (options arrive with Ticket 16).";
        }

        private void OnAddReturn(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) { StatusText.Text = "Select a phase first."; return; }
            var id = "pn-" + _vm.SelectedPhase.Id + "-return-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            PersistPhaseNode(new ReturnNodeDefinition { Id = id });
            StatusText.Text = "Added Return node.";
        }

        private void PersistPhaseNode(PhaseGraphNodeDefinition node, Point? location = null)
        {
            var point = location ?? NextPhasePosition();
            WithConnection(connection =>
            {
                PhaseGraphRepository.AddNode(connection, _vm.SelectedPhase.Id, node);
                AuthoringLayoutRepository.SavePhaseNodePosition(connection, _vm.SelectedPhase.Id, node.Id, point.X, point.Y);
            });
            ReloadPhaseEditor();
        }

        private Point NextPhasePosition()
        {
            var offset = 60.0 + _vm.PhaseGraph.Nodes.Count * 30;
            return new Point(offset, offset);
        }

        // ---------- phase library CRUD + header ----------

        private void OnNewPhase(object sender, RoutedEventArgs e)
        {
            var phase = new PhaseDefinition
            {
                Id = "phase-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Title = "New Phase",
            };
            WithConnection(connection => PhaseRepository.Create(connection, phase.Id, phase.Title));
            _vm.Content.Phases.Add(phase);
            BindPhaseList();
            PhaseList.SelectedItem = phase;
            StatusText.Text = "Created phase '" + phase.Title + "' — add an Entry node to begin.";
        }

        private void OnDeletePhase(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) return;
            var phase = _vm.SelectedPhase;

            var (sessions, _) = WithConnectionResult(connection => PhaseRepository.Usage(connection, phase.Id));
            if (sessions > 0)
            {
                MessageBox.Show(this,
                    "Phase '" + phase.Title + "' is referenced by " + sessions + " session(s). " +
                    "Delete those placements first (schema RESTRICT).",
                    "Cannot Delete Phase", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(this,
                "Delete phase '" + phase.Title + "'? Its low-level graph and layout are removed.",
                "Delete Phase", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            WithConnection(connection => PhaseRepository.Delete(connection, phase.Id));
            _vm.Content.Phases.Remove(phase);
            BindPhaseList();
            if (_vm.Content.Phases.Count > 0) PhaseList.SelectedIndex = 0;
            else _vm.SelectPhase(null);
            StatusText.Text = "Deleted phase '" + phase.Title + "'.";
        }

        private void OnDuplicatePhase(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) return;
            var source = _vm.SelectedPhase;
            var newId = "phase-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var clone = ContentCloner.ClonePhase(source, newId);
            var layout = WithConnectionResult(connection =>
            {
                var remapped = new List<(string NodeId, double X, double Y)>();
                var saved = AuthoringLayoutRepository.LoadPhaseNodePositions(connection, source.Id);
                foreach (var pair in saved)
                {
                    if (clone.NodeIdMap.TryGetValue(pair.Key, out var newNodeId))
                    {
                        remapped.Add((newNodeId, pair.Value.X, pair.Value.Y));
                    }
                }
                return remapped;
            });
            PushCommand(new DuplicatePhaseCommand(OpenConnection, clone.Phase, layout));

            _vm.Content.Phases.Add(clone.Phase);
            BindPhaseList();
            PhaseList.SelectedItem = clone.Phase;
            StatusText.Text = "Duplicated phase — exits, graph, action instances and layout deep-cloned.";
        }

        private void OnShowSessions(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) return;
            var titles = WithConnectionResult(connection =>
                PhaseRepository.ReferencingSessionTitles(connection, _vm.SelectedPhase.Id));
            var (sessions, placements) = WithConnectionResult(connection =>
                PhaseRepository.Usage(connection, _vm.SelectedPhase.Id));
            var body = titles.Count == 0
                ? "(no session references)"
                : string.Join("\n", titles);
            MessageBox.Show(this, $"Referenced by {sessions} session(s) · {placements} placement(s):\n\n{body}",
                "Sessions Using Phase", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnMakeUnique(object sender, RoutedEventArgs e)
        {
            MakeUniqueCurrentPlacement(applyExitEdit: null);
        }

        /// <summary>
        /// Detaches the placement currently selected in the Session graph from its
        /// shared Phase as one undoable MakeUniqueCommand. Optionally composes it
        /// with a pending blocked exit edit (add/delete on the clone) so the whole
        /// operation undoes as a single step (Ticket 18).
        /// </summary>
        private void MakeUniqueCurrentPlacement(Func<string, IAuthoringCommand> applyExitEdit)
        {
            var placement = _vm.SessionGraph.SelectedNode;
            if (placement == null || placement.Kind != "phase-reference" || string.IsNullOrEmpty(placement.RefId))
            {
                StatusText.Text = "Select a PhaseReference placement in the Session canvas first.";
                return;
            }
            var sharedPhase = _vm.Content?.Phases.FirstOrDefault(p => p.Id == placement.RefId);
            if (sharedPhase == null) { StatusText.Text = "Referenced phase not found."; return; }

            var newId = "phase-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var clone = ContentCloner.ClonePhase(sharedPhase, newId);
            var portMap = WithConnectionResult(connection =>
                AuthoringUndo.PlacementExitMap(connection, placement.Id));

            var makeUnique = new MakeUniqueCommand(OpenConnection, sharedPhase, placement.Id, newId, portMap);
            var command = applyExitEdit != null
                ? (IAuthoringCommand)new CompositeCommand("Make unique + edit ports", makeUnique, applyExitEdit(newId))
                : makeUnique;
            var editApplied = applyExitEdit != null;
            PushCommand(command);

            // Update the in-memory placement + content, then reload both canvases.
            placement.RefId = newId;
            _vm.Content.Phases.Add(clone.Phase);
            BindPhaseList();
            _vm.SelectPhaseById(newId);
            _vm.PhaseGraph.HasPlacementContext = true;
            SyncPhaseListSelection(newId);
            ReloadSessionEditor();
            ReloadPhaseEditor();
            UpdatePhaseHeader();
            StatusText.Text = "Made unique — placement now owns its own phase clone" +
                (editApplied ? ", then applied the requested exit edit." : ".");
        }

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

        private T WithConnectionResult<T>(Func<SqliteConnection, T> action)
        {
            try
            {
                var path = ReferencePlayerWindow.ResolveDatabasePath();
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    connection.Open();
                    ConnectionInitializer.Initialize(connection);
                    return action(connection);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Persistence error";
                MessageBox.Show(this, "Database read failed:\n\n" + ex.Message,
                    "Workbench", MessageBoxButton.OK, MessageBoxImage.Error);
                return default;
            }
        }

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
