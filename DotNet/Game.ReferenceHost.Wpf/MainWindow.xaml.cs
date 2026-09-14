using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
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

    /// <summary>Collapses when the bound bool is true — the inverse of BoolToVisible.</summary>
    public sealed class BoolToCollapsedConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class NotNullToVisibleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value == null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class NullToVisibleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class MainWindow : Window
    {
        private readonly WorkbenchViewModel _vm;
        private readonly AuthoringCommandStack _stack = new AuthoringCommandStack();
        private readonly LibraryPaneController _library = new LibraryPaneController();
        private bool _loaded;
        private bool _syncingSessionMeta;
        private bool _syncingPhaseMeta;
        private bool _loadingPlacementPhase;
        private bool _suppressDisconnectCommands;
        private bool _hydratingSessionViewport;
        private bool _hydratingPhaseViewport;
        private int _sessionViewportHydrationGeneration;
        private int _phaseViewportHydrationGeneration;
        private readonly ObservableCollection<ActionTypeChoice> _actionBrowserChoices = new ObservableCollection<ActionTypeChoice>();
        private Point _actionDragStart;
        private bool _actionDragInProgress;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new WorkbenchViewModel();
            DataContext = _vm;
            ActionBrowserList.ItemsSource = _actionBrowserChoices;
            RefreshActionBrowser();

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
                    AuthoringUndo.SessionEdgeFromSource(connection, source.Id));
                PushCommand(new ConnectSessionCommand(OpenConnection, _vm.SelectedSession.Id,
                    source.Id, target.Owner.Id, replaced));
            };

            _vm.SessionGraph.ConnectionRemoved += connection =>
            {
                if (_suppressDisconnectCommands) return;
                if (_vm.SelectedSession == null || connection?.Source?.Owner == null || connection?.Target?.Owner == null) return;
                var edge = WithConnectionResult(db =>
                    AuthoringUndo.SessionEdgeFromSource(db, connection.Source.Id));
                PushCommand(new DisconnectSessionCommand(OpenConnection, _vm.SelectedSession.Id,
                    connection.Source.Id, edge));
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
                if (_hydratingSessionViewport || _vm.SelectedSession == null) return;
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
                    AuthoringUndo.PhaseEdgeFromSource(connection, source.Id));
                PushCommand(new ConnectPhaseCommand(OpenConnection, _vm.SelectedPhase.Id,
                    source.Id, target.Owner.Id, replaced));
            };

            _vm.PhaseGraph.ConnectionRemoved += connection =>
            {
                if (_suppressDisconnectCommands) return;
                if (_vm.SelectedPhase == null || connection?.Source?.Owner == null || connection?.Target?.Owner == null) return;
                var edge = WithConnectionResult(db =>
                    AuthoringUndo.PhaseEdgeFromSource(db, connection.Source.Id));
                PushCommand(new DisconnectPhaseCommand(OpenConnection, _vm.SelectedPhase.Id,
                    connection.Source.Id, edge));
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
                if (_hydratingPhaseViewport || _vm.SelectedPhase == null) return;
                AuthoringLayoutRepository.SaveViewport(connection, "phase", _vm.SelectedPhase.Id,
                    _vm.PhaseGraph.ViewportZoom, _vm.PhaseGraph.ViewportLocation.X, _vm.PhaseGraph.ViewportLocation.Y);
            });

            _vm.PhaseGraph.ActionChanged += (node, row, field) =>
            {
                if (row == null || string.IsNullOrEmpty(row.InstanceId)) return;
                if (field == nameof(PromptChoiceOptionRowData.Label))
                {
                    CommitPromptChoiceChange(row, LoadSequenceSnapshot(row.SequenceId));
                    return;
                }
                if (field != nameof(ActionRowData.TextValue) && field != nameof(ActionRowData.NumberText) &&
                    field != nameof(ActionRowData.SecondaryNumberText) && field != nameof(ActionRowData.PatternValue) &&
                    field != nameof(ActionRowData.IsBlocking)) return;
                PushOrMerge(new UpdateActionInstanceCommand(OpenConnection, row.InstanceId, row.TypeKey,
                    row.PersistedTextValue, ParseFloat(row.PersistedNumberText),
                    row.TextValue, ParseFloat(row.NumberText), ParseFloat(row.PersistedSecondaryNumberText),
                    ParseFloat(row.SecondaryNumberText), row.PersistedPatternValue, row.PatternValue,
                    row.PersistedIsBlocking, row.IsBlocking));
                row.PersistedTextValue = row.TextValue;
                row.PersistedPatternValue = row.PatternValue;
                row.PersistedNumberText = row.NumberText;
                row.PersistedSecondaryNumberText = row.SecondaryNumberText;
                row.PersistedIsBlocking = row.IsBlocking;
            };

            _vm.SessionGraph.ActionChanged += (node, row, field) =>
            {
                if (row == null || string.IsNullOrEmpty(row.InstanceId)) return;
                if (field == nameof(PromptChoiceOptionRowData.Label))
                {
                    CommitPromptChoiceChange(row, LoadSequenceSnapshot(row.SequenceId));
                    return;
                }
                if (field != nameof(ActionRowData.TextValue) && field != nameof(ActionRowData.NumberText) &&
                    field != nameof(ActionRowData.SecondaryNumberText) && field != nameof(ActionRowData.PatternValue) &&
                    field != nameof(ActionRowData.IsBlocking)) return;
                PushOrMerge(new UpdateActionInstanceCommand(OpenConnection, row.InstanceId, row.TypeKey,
                    row.PersistedTextValue, ParseFloat(row.PersistedNumberText),
                    row.TextValue, ParseFloat(row.NumberText), ParseFloat(row.PersistedSecondaryNumberText),
                    ParseFloat(row.SecondaryNumberText), row.PersistedPatternValue, row.PatternValue,
                    row.PersistedIsBlocking, row.IsBlocking));
                row.PersistedTextValue = row.TextValue;
                row.PersistedPatternValue = row.PatternValue;
                row.PersistedNumberText = row.NumberText;
                row.PersistedSecondaryNumberText = row.SecondaryNumberText;
                row.PersistedIsBlocking = row.IsBlocking;
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
            if (!CanApplyCatalogMutation(_stack.NextUndo, undo: true)) return;
            var isInMemoryEdit = _stack.NextUndo is InMemorySequenceCommand;
            try
            {
                _stack.Undo();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Undo failed:\n\n" + ex.Message,
                    "Undo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (!isInMemoryEdit) ReloadAllFromDb();
        }

        private void Redo()
        {
            if (!_stack.CanRedo) return;
            if (!CanApplyCatalogMutation(_stack.NextRedo, undo: false)) return;
            var isInMemoryEdit = _stack.NextRedo is InMemorySequenceCommand;
            try
            {
                _stack.Redo();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Redo failed:\n\n" + ex.Message,
                    "Redo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (!isInMemoryEdit) ReloadAllFromDb();
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
            RefreshActionAuthoringCatalogs();
        }

        private bool CanApplyCatalogMutation(IAuthoringCommand command, bool undo)
        {
            if (!(command is ICatalogMutationCommand mutation)) return true;
            if (undo ? !mutation.DeletesOnUndo : !mutation.DeletesOnExecute) return true;
            var message = UnsavedCatalogReferenceMessage(mutation.CatalogKind, mutation.CatalogId);
            if (message == null) return true;
            StatusText.Text = "Catalog change blocked by unsaved Card reference.";
            MessageBox.Show(this, message, "Unsaved Card", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private string UnsavedCatalogReferenceMessage(string kind, string id)
        {
            if (_cardBuffer == null || string.IsNullOrEmpty(id)) return null;
            if (kind == CatalogKinds.Resource && _cardBuffer.ReferencesResource(id))
                return "Delete blocked:\nResource is referenced by the currently edited unsaved Card.";
            if (kind == CatalogKinds.DialogTag && _cardBuffer.ReferencesDialogTag(id))
                return "Delete blocked:\nDialog Tag is referenced by the currently edited unsaved Card.";
            if (kind == CatalogKinds.PerformanceEvent && _cardBuffer.ReferencesPerformanceEvent(id))
                return "Delete blocked:\nPerformance Event is performed by the currently edited unsaved Card.";
            if (kind == CatalogKinds.SmartToyCapability && _cardBuffer.ReferencesSmartToyCapability(id))
                return "This capability is referenced by the currently edited unsaved Card.";
            return null;
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

        private void OnAddActionInstance(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is ActionSequenceEditorViewModel sequence)) return;
            if (string.IsNullOrEmpty(sequence.SelectedActionTypeKey) || sequence.OwnerNode == null) return;
            var typeKey = sequence.SelectedActionTypeKey;
            if (typeKey == ActionTypeKeys.ModifyTemperature && sequence.TemperatureOptions.Count == 0)
            {
                StatusText.Text = "Add a Temperature definition before authoring Modify Temperature.";
                return;
            }
            if (typeKey == ActionTypeKeys.Cutscene && sequence.ResourceOptions.Count == 0)
            {
                StatusText.Text = "Add a cutscene Resource before authoring Play Cutscene.";
                return;
            }
            var creationError = ActionAuthoringGuards.CreationError(sequence, typeKey);
            if (creationError != null)
            {
                StatusText.Text = creationError;
                return;
            }

            // Card editor rows edit the BUFFER only; Save Card applies them.
            if (sequence.OwnerScope == ActionOwnerScope.CardSequence)
            {
                AddBufferedCardAction(sequence, typeKey);
                return;
            }

            var instanceId = sequence.IdentityPrefix + "-action-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var instance = sequence.CreateDefaultInstance(typeKey, instanceId);
            var reloadSession = sequence.IsSessionDecisionOption;
            if (typeKey == ActionTypeKeys.SessionGoto)
            {
                PushCommand(new AddSessionGotoCommand(OpenConnection, sequence.OwnerNode.Id,
                    sequence.OptionId, ((SessionGotoInstanceDefinition)instance).Label), reloadSession: true);
            }
            else
            {
                PushCommand(new AddActionInstanceCommand(OpenConnection, sequence.SequenceId,
                    sequence.OwnerScope, typeKey, instanceId, instance), reloadSession, !reloadSession);
            }
            StatusText.Text = "Added " + ActionTypeRegistry.ByTypeKey(typeKey).DisplayLabel + " action.";
        }

        private void OnRemoveActionInstance(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is ActionRowData row)) return;
            if (row.Owner == null || row.Definition == null || row.Sequence == null) return;

            // Card editor rows edit the BUFFER only.
            if (row.Sequence.OwnerScope == ActionOwnerScope.CardSequence)
            {
                _cardBuffer?.RemoveAction(row.SequenceId, row.InstanceId);
                RebuildCardSequenceHost();
                return;
            }

            if (row.TypeKey == ActionTypeKeys.SessionGoto && row.Sequence.IsSessionDecisionOption)
            {
                var snapshot = WithConnectionResult(connection => AuthoringUndo.SnapshotSessionGoto(connection, row.InstanceId));
                PushCommand(new RemoveSessionGotoCommand(OpenConnection, snapshot), reloadSession: true);
            }
            else
            {
                PushCommand(new RemoveActionInstanceCommand(OpenConnection, row.SequenceId, row.Definition, row.Ordinal),
                    reloadSession: row.Sequence.IsSessionDecisionOption, reloadPhase: !row.Sequence.IsSessionDecisionOption);
            }
            StatusText.Text = "Removed " + row.DisplayLabel + " action.";
        }

        private void OnMoveActionUp(object sender, RoutedEventArgs e) => MoveAction(sender, -1);
        private void OnMoveActionDown(object sender, RoutedEventArgs e) => MoveAction(sender, 1);

        private void MoveAction(object sender, int delta)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is ActionRowData row)) return;
            if (row.Sequence == null) return;

            // Card editor rows edit the BUFFER only.
            if (row.Sequence.OwnerScope == ActionOwnerScope.CardSequence)
            {
                if (_cardBuffer != null && _cardBuffer.MoveAction(row.SequenceId, row.InstanceId, delta))
                {
                    RebuildCardSequenceHost();
                }
                return;
            }

            var index = row.Sequence.Rows.IndexOf(row);
            var otherIndex = index + delta;
            if (index < 0 || otherIndex < 0 || otherIndex >= row.Sequence.Rows.Count) return;
            var other = row.Sequence.Rows[otherIndex];
            PushCommand(new MoveActionInstanceCommand(OpenConnection, row.SequenceId, row.InstanceId, other.InstanceId),
                reloadSession: row.Sequence.IsSessionDecisionOption, reloadPhase: !row.Sequence.IsSessionDecisionOption);
        }

        private void OnDuplicateActionInstance(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is ActionRowData row)) return;
            if (row.Sequence == null) return;
            var sequence = row.Sequence;

            // Card editor rows edit the BUFFER only.
            if (sequence.OwnerScope == ActionOwnerScope.CardSequence)
            {
                var instanceId = (sequence.IdentityPrefix ?? "card") + "-action-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var duplicate = ActionInstanceCloneUtility.Clone(row.Definition, instanceId);
                _cardBuffer?.AddConfiguredAction(_cardBuffer.FindSequence(sequence.SequenceId) ?? _cardBuffer.Sequence, duplicate);
                RebuildCardSequenceHost();
                return;
            }

            if (row.TypeKey == ActionTypeKeys.SessionGoto && sequence.IsSessionDecisionOption)
            {
                PushCommand(new AddSessionGotoCommand(OpenConnection, sequence.OwnerNode.Id,
                    sequence.OptionId, row.TextValue ?? ""), reloadSession: true);
            }
            else
            {
                var instanceId = sequence.IdentityPrefix + "-action-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var duplicate = ActionInstanceCloneUtility.Clone(row.Definition, instanceId);
                PushCommand(new AddActionInstanceCommand(OpenConnection, row.SequenceId,
                    sequence.OwnerScope, row.TypeKey, instanceId, duplicate, row.Ordinal + 1),
                    reloadSession: sequence.IsSessionDecisionOption, reloadPhase: !sequence.IsSessionDecisionOption);
            }
            StatusText.Text = "Duplicated " + row.DisplayLabel + " action.";
        }

        private void RefreshActionBrowser()
        {
            _actionBrowserChoices.Clear();
            var query = (ActionBrowserSearchBox?.Text ?? "").Trim();
            foreach (var choice in ActionEditorRegistry.PickerChoices(ActionOwnerScope.All))
            {
                if (query.Length == 0 || choice.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    _actionBrowserChoices.Add(choice);
            }
        }

        private void RefreshActionAuthoringCatalogs()
        {
            if (_vm?.Content == null) return;
            _vm.RefreshActionAuthoringCatalogs();
            if (CardActionSequenceHost?.Content is ActionSequenceEditorViewModel cardEditor)
                cardEditor.RefreshCatalogs(_vm.Content);
            RefreshVisibleDialogTagPickers(this);
        }

        private static void RefreshVisibleDialogTagPickers(DependencyObject root)
        {
            if (root == null) return;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is RelationPickerControl picker && picker.DataContext is ActionRowData)
                    ConfigureActionDialogTagPicker(picker);
                RefreshVisibleDialogTagPickers(child);
            }
        }

        private void OnActionBrowserSearchChanged(object sender, TextChangedEventArgs e) => RefreshActionBrowser();

        private void OnActionStrictChoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox combo) || !(combo.DataContext is ActionRowData row)) return;
            // WPF raises selection events while an ItemsSource is being
            // reattached. Only a focused/open control represents author input;
            // all other events must leave the persisted row value untouched.
            if (!combo.IsKeyboardFocusWithin && !combo.IsDropDownOpen) return;
            if (!(combo.SelectedItem is ActionParameterOption option)) return;
            row.TextValue = option.Id ?? "";
        }

        private void OnActionPatternChoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox combo) || !(combo.DataContext is ActionRowData row)) return;
            if (!combo.IsKeyboardFocusWithin && !combo.IsDropDownOpen) return;
            if (combo.SelectedItem is ActionParameterOption option)
                row.PatternValue = option.Id ?? "";
        }

        private void OnActionBlockingChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox check) || !(check.DataContext is ActionRowData row)) return;
            if (!check.IsKeyboardFocusWithin) return;
            row.IsBlocking = check.IsChecked == true;
        }

        private void OnActionDialogTagPickerLoaded(object sender, RoutedEventArgs e)
        {
            ConfigureActionDialogTagPicker(sender as RelationPickerControl);
        }

        private void OnActionDialogTagPickerDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ConfigureActionDialogTagPicker(sender as RelationPickerControl);
        }

        private static void ConfigureActionDialogTagPicker(RelationPickerControl picker)
        {
            if (picker?.DataContext is ActionRowData row)
            {
                var ids = (row.PatternValue ?? "")
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0);
                picker.SetItems(row.DialogTagOptions, ids);
            }
        }

        private void OnActionDialogTagsChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is RelationPickerControl picker) || !(picker.DataContext is ActionRowData row)) return;
            row.PatternValue = string.Join(";", picker.SelectedIds);
        }

        private void OnActionTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is Control control) || control.Visibility != Visibility.Visible ||
                !control.IsKeyboardFocusWithin || !(control.DataContext is ActionRowData row)) return;
            row.TextValue = sender is TextBox textBox ? textBox.Text : ((ComboBox)sender).Text;
        }

        private void OnActionEditableChoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox combo) || combo.Visibility != Visibility.Visible ||
                !combo.IsKeyboardFocusWithin) return;
            combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        }

        private void OnActionEditableChoiceLostFocus(object sender, RoutedEventArgs e)
        {
            if (!(sender is ComboBox combo) || combo.Visibility != Visibility.Visible) return;
            combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        }

        private void OnActionNumberChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is Control control) || control.Visibility != Visibility.Visible ||
                !control.IsKeyboardFocusWithin || !(control.DataContext is ActionRowData row)) return;
            row.NumberText = ((TextBox)sender).Text;
        }

        private void OnActionSecondaryNumberChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is Control control) || control.Visibility != Visibility.Visible ||
                !control.IsKeyboardFocusWithin || !(control.DataContext is ActionRowData row)) return;
            row.SecondaryNumberText = ((TextBox)sender).Text;
        }

        private void OnActionDragStart(object sender, MouseButtonEventArgs e)
        {
            _actionDragStart = e.GetPosition(sender as IInputElement);
        }

        private void OnActionBrowserMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !(ActionBrowserList.SelectedItem is ActionTypeChoice choice)) return;
            var point = e.GetPosition(ActionBrowserList);
            if (Math.Abs(point.X - _actionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _actionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            RunActionDrag(ActionBrowserList, new DataObject(typeof(ActionTypeChoice), choice), DragDropEffects.Copy);
        }

        private void OnActionRowMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !(sender is FrameworkElement element) ||
                !(element.DataContext is ActionRowData row)) return;
            var point = e.GetPosition(element);
            if (Math.Abs(point.X - _actionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _actionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                _actionDragStart = point;
                return;
            }
            RunActionDrag(element, new DataObject(typeof(ActionRowData), row), DragDropEffects.Copy | DragDropEffects.Move);
        }

        private void OnActionDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(ActionBlockBrowserItem)) ||
                        e.Data.GetDataPresent(typeof(ActionTypeChoice)) ||
                        e.Data.GetDataPresent(typeof(ActionRowData)) ||
                        e.Data.GetDataPresent(typeof(ResourceDefinition))
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void RunActionDrag(DependencyObject source, DataObject data, DragDropEffects effects)
        {
            if (_actionDragInProgress) return;
            _actionDragInProgress = true;
            try
            {
                DragDrop.DoDragDrop(source, data, effects);
            }
            finally
            {
                _actionDragInProgress = false;
            }
        }

        private void OnActionDrop(object sender, DragEventArgs e)
        {
            var targetSequence = (sender as FrameworkElement)?.DataContext as ActionSequenceEditorViewModel;
            var targetIndex = targetSequence?.Rows.Count ?? 0;
            ActionRowData targetRow = null;
            if (sender is FrameworkElement targetElement && targetElement.DataContext is ActionRowData row)
            {
                targetRow = row;
                targetSequence = row.Sequence;
                targetIndex = targetSequence.Rows.IndexOf(targetRow);
            }
            if (targetSequence == null) return;

            if (e.Data.GetDataPresent(typeof(ActionBlockBrowserItem)))
            {
                var block = e.Data.GetData(typeof(ActionBlockBrowserItem)) as ActionBlockBrowserItem;
                if (block != null)
                {
                    InsertActionBlock(block.Block, targetSequence, targetIndex);
                    e.Handled = true;
                }
                return;
            }

            if (e.Data.GetDataPresent(typeof(ResourceDefinition)))
            {
                var resource = (ResourceDefinition)e.Data.GetData(typeof(ResourceDefinition));
                if (targetRow == null || resource == null)
                {
                    StatusText.Text = "Drop the resource onto an authored action row.";
                    e.Handled = true;
                    return;
                }
                if (resource.Kind.Equals(ResourceKinds.Cutscene, StringComparison.OrdinalIgnoreCase) &&
                    targetRow.TypeKey == ActionTypeKeys.Cutscene)
                {
                    targetRow.TextValue = resource.Id;
                    e.Handled = true;
                    return;
                }
                if (resource.Kind.Equals(ResourceKinds.ToyPattern, StringComparison.OrdinalIgnoreCase) &&
                    (targetRow.TypeKey == ActionTypeKeys.ToyActivity || targetRow.TypeKey == ActionTypeKeys.ToySetPattern))
                {
                    targetRow.PatternValue = resource.Id;
                    e.Handled = true;
                    return;
                }
                StatusText.Text = "Resource kind is incompatible with this action row.";
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(typeof(ActionTypeChoice)))
            {
                var choice = (ActionTypeChoice)e.Data.GetData(typeof(ActionTypeChoice));
                AppendBrowserAction(choice, targetSequence, targetIndex);
                e.Handled = true;
                return;
            }
            if (e.Data.GetDataPresent(typeof(ActionRowData)))
            {
                var sourceRow = (ActionRowData)e.Data.GetData(typeof(ActionRowData));
                ReorderOrCopyAction(sourceRow, targetSequence, targetIndex);
                e.Handled = true;
            }
        }

        private void AppendBrowserAction(ActionTypeChoice choice, ActionSequenceEditorViewModel sequence, int ordinal)
        {
            if (choice == null || sequence == null || sequence.OwnerNode == null) return;
            var creationError = ActionAuthoringGuards.CreationError(sequence, choice.TypeKey);
            if (creationError != null)
            {
                StatusText.Text = creationError;
                return;
            }
            var instanceId = sequence.IdentityPrefix + "-action-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var instance = sequence.CreateDefaultInstance(choice.TypeKey, instanceId);
            try
            {
                ActionTypeRegistry.ValidateScope(instance, sequence.OwnerScope);
                if (sequence.OwnerScope == ActionOwnerScope.CardSequence)
                {
                    _cardBuffer?.InsertConfiguredAction(_cardBuffer.FindSequence(sequence.SequenceId) ?? _cardBuffer.Sequence, instance, ordinal);
                    RebuildCardSequenceHost();
                }
                else if (choice.TypeKey == ActionTypeKeys.SessionGoto && sequence.IsSessionDecisionOption)
                {
                    PushCommand(new AddSessionGotoCommand(OpenConnection, sequence.OwnerNode.Id, sequence.OptionId,
                        ((SessionGotoInstanceDefinition)instance).Label), reloadSession: true);
                }
                else
                {
                    PushCommand(new AddActionInstanceCommand(OpenConnection, sequence.SequenceId, sequence.OwnerScope,
                        choice.TypeKey, instanceId, instance, ordinal),
                        reloadSession: sequence.IsSessionDecisionOption, reloadPhase: !sequence.IsSessionDecisionOption);
                }
                StatusText.Text = "Added " + choice.DisplayLabel + " action.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Action rejected: " + ex.Message;
            }
        }

        private void ReorderOrCopyAction(ActionRowData source, ActionSequenceEditorViewModel target, int targetIndex)
        {
            if (source?.Definition == null || target == null) return;
            if (ReferenceEquals(source.Sequence, target))
            {
                var current = source.Sequence.Rows.IndexOf(source);
                var normalized = targetIndex;
                if (current >= 0 && current < normalized) normalized--;
                if (current < 0 || current == normalized) return;
                if (target.OwnerScope == ActionOwnerScope.CardSequence)
                {
                    if (_cardBuffer?.MoveActionTo(target.SequenceId, source.InstanceId, normalized) == true) RebuildCardSequenceHost();
                }
                else
                {
                    PushCommand(new MoveActionInstanceToOrdinalCommand(OpenConnection, target.SequenceId,
                        source.InstanceId, current, normalized),
                        reloadSession: target.IsSessionDecisionOption, reloadPhase: !target.IsSessionDecisionOption);
                }
                return;
            }

            var newId = target.IdentityPrefix + "-action-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var copy = ActionInstanceCloneUtility.Clone(source.Definition, newId);
            try
            {
                ActionTypeRegistry.ValidateScope(copy, target.OwnerScope);
                if (target.OwnerScope == ActionOwnerScope.CardSequence)
                {
                    _cardBuffer?.InsertConfiguredAction(_cardBuffer.FindSequence(target.SequenceId) ?? _cardBuffer.Sequence, copy, targetIndex);
                    RebuildCardSequenceHost();
                }
                else if (copy is SessionGotoInstanceDefinition && target.IsSessionDecisionOption)
                {
                    PushCommand(new AddSessionGotoCommand(OpenConnection, target.OwnerNode.Id, target.OptionId,
                        ((SessionGotoInstanceDefinition)copy).Label), reloadSession: true);
                }
                else
                {
                    PushCommand(new AddActionInstanceCommand(OpenConnection, target.SequenceId, target.OwnerScope,
                        target.Rows.Count > 0 ? ActionTypeRegistry.ForInstance(copy).TypeKey : ActionTypeRegistry.ForInstance(copy).TypeKey,
                        newId, copy, targetIndex),
                        reloadSession: target.IsSessionDecisionOption, reloadPhase: !target.IsSessionDecisionOption);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Copy rejected: " + ex.Message;
            }
        }

        private void OnTogglePromptChoice(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ActionRowData row)
                row.IsExpanded = !row.IsExpanded;
        }

        private void OnAddPromptChoiceOption(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is ActionRowData row) ||
                !(row.Definition is PromptChoiceInstanceDefinition choice) || row.Sequence == null) return;
            if (choice.Options.Count >= 3)
            {
                StatusText.Text = "PromptChoice supports at most three options.";
                return;
            }
            var before = _cardBuffer != null && row.Sequence.OwnerScope == ActionOwnerScope.CardSequence
                ? SequenceSnapshotUtility.Clone(_cardBuffer.FindSequence(row.SequenceId))
                : LoadSequenceSnapshot(row.SequenceId);
            var optionId = row.InstanceId + "-option-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            choice.Options.Add(new PromptChoiceOptionDefinition
            {
                Id = optionId,
                Label = "Option " + (choice.Options.Count + 1),
                Sequence = new ActionSequenceDefinition { Id = optionId + "-sequence" },
            });
            CommitPromptChoiceChange(row, before);
        }

        private void OnRemovePromptChoiceOption(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is PromptChoiceOptionRowData option) ||
                option.Parent?.Definition is not PromptChoiceInstanceDefinition choice || option.Parent.Sequence == null) return;
            if (choice.Options.Count <= 1)
            {
                StatusText.Text = "PromptChoice must keep at least one option.";
                return;
            }
            var before = _cardBuffer != null && option.Parent.Sequence.OwnerScope == ActionOwnerScope.CardSequence
                ? SequenceSnapshotUtility.Clone(_cardBuffer.FindSequence(option.Parent.SequenceId))
                : LoadSequenceSnapshot(option.Parent.SequenceId);
            choice.Options.RemoveAll(item => item.Id == option.OptionId);
            CommitPromptChoiceChange(option.Parent, before);
        }

        private void CommitPromptChoiceChange(ActionRowData row, ActionSequenceDefinition before)
        {
            if (_cardBuffer != null && row.Sequence.OwnerScope == ActionOwnerScope.CardSequence)
            {
                RebuildCardSequenceHost();
                return;
            }
            var after = SequenceSnapshotUtility.Clone(row.Sequence);
            PushCommand(new ReplaceActionSequenceContentsCommand(OpenConnection, before, after),
                reloadSession: row.Sequence.IsSessionDecisionOption, reloadPhase: !row.Sequence.IsSessionDecisionOption);
        }

        private ActionSequenceDefinition LoadSequenceSnapshot(string sequenceId)
        {
            return WithConnectionResult(connection =>
            {
                var content = GameContentSnapshotLoader.Load(connection);
                foreach (var card in content.Cards)
                {
                    var found = FindSequence(card.Sequence, sequenceId);
                    if (found != null) return SequenceSnapshotUtility.Clone(found);
                }
                foreach (var phase in content.Phases)
                {
                    foreach (var node in phase.Graph?.Nodes ?? new List<GraphNodeDefinition>())
                    {
                        foreach (var sequence in PhaseSequencesOf(node as PhaseGraphNodeDefinition))
                        {
                            var found = FindSequence(sequence, sequenceId);
                            if (found != null) return SequenceSnapshotUtility.Clone(found);
                        }
                    }
                }
                foreach (var session in content.Sessions)
                {
                    foreach (var node in session.Graph?.Nodes ?? new List<SessionGraphNodeDefinition>())
                    {
                        foreach (var sequence in SessionSequencesOf(node))
                        {
                            var found = FindSequence(sequence, sequenceId);
                            if (found != null) return SequenceSnapshotUtility.Clone(found);
                        }
                    }
                }
                throw new InvalidOperationException("Action sequence not found: " + sequenceId);
            });
        }

        private static ActionSequenceDefinition FindSequence(ActionSequenceDefinition sequence, string sequenceId)
        {
            if (sequence == null) return null;
            if (sequence.Id == sequenceId) return sequence;
            foreach (var instance in sequence.Instances)
                if (instance is PromptChoiceInstanceDefinition choice)
                    foreach (var option in choice.Options)
                    {
                        var found = FindSequence(option.Sequence, sequenceId);
                        if (found != null) return found;
                    }
            return null;
        }

        private static IEnumerable<ActionSequenceDefinition> PhaseSequencesOf(PhaseGraphNodeDefinition node)
        {
            if (node is ActionNodeDefinition action && action.Sequence != null) yield return action.Sequence;
            if (node is PhaseDecisionNodeDefinition decision)
                foreach (var option in decision.Options)
                    if (option.Sequence != null) yield return option.Sequence;
        }

        private static IEnumerable<ActionSequenceDefinition> SessionSequencesOf(SessionGraphNodeDefinition node)
        {
            if (node is SessionDecisionNodeDefinition decision)
                foreach (var option in decision.Options)
                    if (option.Sequence != null) yield return option.Sequence;
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
                SessionWeightingPanel.Visibility = Visibility.Collapsed;
                PhaseMetaPanel.Visibility = Visibility.Collapsed;
                return;
            }
            InspTitle.Text = node.Title;
            InspId.Text = string.IsNullOrEmpty(node.RefId)
                ? "id: " + node.Id
                : "id: " + node.Id + "\nref: " + node.RefId;
            InspKind.Text = node.Kind + (string.IsNullOrEmpty(node.Subtitle) ? "" : "\n" + node.Subtitle);
            // Selecting the Start node exposes the Session metadata (title + type + weighting).
            SessionMetaPanel.Visibility = node.Kind == "start" ? Visibility.Visible : Visibility.Collapsed;
            SessionWeightingPanel.Visibility = node.Kind == "start" ? Visibility.Visible : Visibility.Collapsed;
            if (node.Kind == "start")
            {
                SyncSessionMetaPanel();
                SyncSessionWeightingPanel();
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
                var choices = _vm.Content.CardTagDefinitions
                    .Select(tag => new RelationChoice { Id = tag.Id, DisplayName = tag.Title })
                    .ToList();
                PhaseAllTagPicker.SetItems(choices, _vm.SelectedPhase.MustHaveAllCardTags);
                PhaseAnyTagPicker.SetItems(choices, _vm.SelectedPhase.MustHaveAnyCardTags);
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

        private void OnPhaseTagsChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingPhaseMeta || _vm.SelectedPhase == null) return;
            var all = PhaseAllTagPicker.SelectedIds.ToList();
            var any = PhaseAnyTagPicker.SelectedIds.ToList();

            PushOrMerge(new SetPhaseCardQueryCommand(OpenConnection, _vm.SelectedPhase.Id,
                _vm.SelectedPhase.MustHaveAllCardTags, _vm.SelectedPhase.MustHaveAnyCardTags, all, any));
            _vm.SelectedPhase.MustHaveAllCardTags = all;
            _vm.SelectedPhase.MustHaveAnyCardTags = any;
            StatusText.Text = "Phase card query updated.";
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
                    ConnectionInitializer.Initialize(connection);
                    CoreMigrator.EnsureSchema(connection);
                    content = GameContentSnapshotLoader.Load(connection);
                }
                _vm.LoadContent(content);
                _vm.PresentationCatalog = TryLoadPresentationCatalog(path);
                BindLibrary();
                ReloadSessionEditor();
                ReloadPhaseEditor();
                StatusText.Text = $"Content loaded from {path} — {content.Sessions.Count} sessions · {content.Phases.Count} phases";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Content error";
                MessageBox.Show(this, "Failed to load content:\n\n" + ex.Message,
                    "Workbench", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static PresentationCatalogDefinition TryLoadPresentationCatalog(string databasePath)
        {
            return PresentationCatalogLoader.TryLoad(databasePath);
        }

        private void BindLibrary()
        {
            // A content reload must not navigate the Library. Capture the
            // currently visible drawer before rebinding its data sources.
            var showPhases = PhaseLibraryPanel.Visibility == Visibility.Visible;
            var showCards = CardsLibraryPanel.Visibility == Visibility.Visible;
            var showCatalogs = CatalogsLibraryPanel.Visibility == Visibility.Visible;

            BindSessionList();
            BindPhaseList();
            BindCardList();
            BindCatalogs();
            BindSessionTypeBox();

            if (showCatalogs) OnShowCatalogsLibrary(this, new RoutedEventArgs());
            else if (showCards) OnShowCardsLibrary(this, new RoutedEventArgs());
            else SetLibraryMode(!showPhases);
        }

        private void OnShowSessionLibrary(object sender, RoutedEventArgs e) => SetLibraryMode(true);

        private void OnShowPhaseLibrary(object sender, RoutedEventArgs e) => SetLibraryMode(false);

        private void OnLibraryItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || !(sender is ListBox list)) return;
            var source = e.OriginalSource as DependencyObject ?? e.Source as DependencyObject;
            if (source == null) return;
            var container = ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
            if (container?.DataContext == null) return;
            container.IsSelected = true;

            if (container.DataContext is SessionDefinition session)
            {
                var newTitle = PromptForLibraryRename("Session", session.Title);
                if (newTitle == null || string.Equals(newTitle, session.Title, StringComparison.Ordinal)) return;
                PushOrMergeWithReload(new RenameSessionCommand(OpenConnection, session.Id, session.Title ?? "", newTitle), () =>
                {
                    session.Title = newTitle;
                    BindSessionList();
                    SessionList.SelectedItem = session;
                    if (_vm.SelectedSession?.Id == session.Id)
                    {
                        SessionHeader.Text = "Session Graph — " + newTitle;
                        SyncSessionMetaPanel();
                    }
                    StatusText.Text = "Renamed session to '" + newTitle + "'.";
                });
                return;
            }

            if (container.DataContext is PhaseDefinition phase)
            {
                var newTitle = PromptForLibraryRename("Phase", phase.Title);
                if (newTitle == null || string.Equals(newTitle, phase.Title, StringComparison.Ordinal)) return;
                PushOrMergeWithReload(new RenamePhaseCommand(OpenConnection, phase.Id, phase.Title ?? "", newTitle), () =>
                {
                    phase.Title = newTitle;
                    BindPhaseList();
                    PhaseList.SelectedItem = phase;
                    if (_vm.SelectedPhase?.Id == phase.Id)
                    {
                        PhaseHeader.Text = "Phase Graph — " + newTitle;
                        SyncPhaseMetaPanel();
                    }
                    StatusText.Text = "Renamed phase to '" + newTitle + "'.";
                });
                return;
            }

            var oldCatalogValue = CaptureCatalogEdit(container.DataContext);
            if (oldCatalogValue == null) return;
            var catalogTitle = PromptForLibraryRename("Catalog Entry", oldCatalogValue.Title);
            if (catalogTitle == null || string.Equals(catalogTitle, oldCatalogValue.Title, StringComparison.Ordinal)) return;
            var newCatalogValue = CaptureCatalogEdit(container.DataContext);
            newCatalogValue.Title = catalogTitle;
            PushOrMergeWithReload(new UpdateCatalogEntryCommand(OpenConnection, oldCatalogValue, newCatalogValue), () =>
            {
                ApplyCatalogEdit(container.DataContext, newCatalogValue);
                BindCatalogEntries();
                SelectCatalogEntry(newCatalogValue.Id);
                BindSessionTypeBox();
                if (newCatalogValue.Kind == CatalogKinds.SmartToyCapability || newCatalogValue.Kind == CatalogKinds.DialogTag)
                    RefreshActionAuthoringCatalogs();
                StatusText.Text = "Renamed catalog entry to '" + catalogTitle + "'.";
            });
        }

        private string PromptForLibraryRename(string kind, string currentTitle)
        {
            var dialog = new TextInputDialog("Rename " + kind, "Title:", currentTitle ?? "") { Owner = this };
            if (dialog.ShowDialog() != true) return null;
            var title = dialog.InputText?.Trim();
            if (!string.IsNullOrWhiteSpace(title)) return title;
            MessageBox.Show(this, "A title is required.", "Rename " + kind,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        private void OnLibraryItemRightClick(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is ListBox list) || !(e.OriginalSource is DependencyObject source)) return;
            var item = ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
            if (item == null) return;
            item.IsSelected = true;
            item.Focus();
        }

        private void OnLibraryContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (!(sender is ListBox list) || !(Mouse.DirectlyOver is DependencyObject source) ||
                !(ItemsControl.ContainerFromElement(list, source) is ListBoxItem))
            {
                e.Handled = true;
            }
        }

        private void SetLibraryMode(bool sessions)
        {
            if (sessions) _library.ShowSessions(); else _library.ShowPhases();
            SessionLibraryPanel.Visibility = sessions ? Visibility.Visible : Visibility.Collapsed;
            PhaseLibraryPanel.Visibility = sessions ? Visibility.Collapsed : Visibility.Visible;
            HideMilestoneBLibraryPanels();
            if (sessions)
            {
                SessionModeText.Text = string.IsNullOrEmpty(_library.SessionUsageFilterPhaseId)
                    ? "Sessions" : SessionModeText.Text;
                BindSessionList();
            }
            else
            {
                BindPhaseList();
            }
        }

        private void BindPhaseList()
        {
            var shown = _library.FilterPhases(_vm.Content, PhaseFilter.Text);
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
            var shown = _library.FilterSessions(_vm.Content, SessionFilter.Text);
            SessionList.ItemsSource = shown;
            SessionList.DisplayMemberPath = nameof(SessionDefinition.Title);
        }

        private void OnSessionFilterChanged(object sender, TextChangedEventArgs e)
        {
            BindSessionList();
        }

        private void OnClearSessionUsageFilter(object sender, RoutedEventArgs e)
        {
            _library.ClearSessionUsageFilter();
            SessionModeText.Text = "Sessions";
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
                PromptSharedPortEdit(sessions, placements, newPhaseId =>
                {
                    var clonePreview = ContentCloner.ClonePhase(_vm.SelectedPhase, newPhaseId);
                    if (!clonePreview.ExitIdMap.TryGetValue(row.Id, out var cloneExitId))
                        throw new InvalidOperationException("The selected PhaseExit is missing from the unique clone.");
                    return new DeleteExitOnCloneCommand(OpenConnection, newPhaseId, cloneExitId);
                });
                return;
            }

            var projectedEdges = WithConnectionResult(connection =>
                AuthoringUndo.ExitProjectedEdges(connection, row.Id));
            var gotoCount = WithConnectionResult(connection =>
                AuthoringUndo.SnapshotPhaseGotosByExit(connection, row.Id).Count);
            if (gotoCount > 0 || projectedEdges.Count > 0)
            {
                var dialog = new PhaseExitDeleteDialog(row.Name, gotoCount, projectedEdges.Count) { Owner = this };
                if (dialog.ShowDialog() != true) return;
            }

            // The command captures the referenced GOTO actions and projected
            // edges, then clears them atomically. Undo restores exact identities.
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
                var dialog = new SharedPhaseTopologyDialog(sessions, placements) { Owner = this };
                if (dialog.ShowDialog() == true)
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
                var filled = GraphAutoLayout.FillMissing(graph.Nodes, graph.Edges, layout);
                foreach (var pair in filled)
                {
                    if (!LayoutPositionMatches(layout, pair.Key, pair.Value))
                        AuthoringLayoutRepository.SaveSessionNodePosition(connection, _vm.SelectedSession.Id, pair.Key, pair.Value.X, pair.Value.Y);
                }
                var viewport = AuthoringLayoutRepository.LoadViewport(connection, "session", _vm.SelectedSession.Id);
                var sessionId = _vm.SelectedSession.Id;
                var hydrationGeneration = ++_sessionViewportHydrationGeneration;
                _hydratingSessionViewport = true;
                _vm.SelectedSession.Graph = graph;
                _vm.SessionGraph.LoadFromDefinition(_vm.SelectedSession, filled, viewport);
                _vm.SessionGraph.LoadPortalPairs(GraphPortalRepository.LoadSession(connection, _vm.SelectedSession.Id));
                ApplyPendingActionSelection(_vm.SessionGraph);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (hydrationGeneration != _sessionViewportHydrationGeneration) return;
                    if (_vm.SelectedSession?.Id == sessionId && viewport.HasValue)
                    {
                        _vm.SessionGraph.ViewportZoom = viewport.Value.Zoom;
                        _vm.SessionGraph.ViewportLocation = new Point(viewport.Value.X, viewport.Value.Y);
                    }
                    _hydratingSessionViewport = false;
                }));
            });
        }

        private void ReloadPhaseEditor()
        {
            if (_vm.SelectedPhase == null) return;
            WithConnection(connection =>
            {
                // The selected PhaseDefinition is the live library object and
                // can still hold the graph from before the command executed.
                // Read the graph back from SQLite before rebuilding the canvas;
                // otherwise a successful insert stays invisible until restart.
                var freshPhase = GameContentSnapshotLoader.Load(connection).Phases
                    .FirstOrDefault(phase => phase.Id == _vm.SelectedPhase.Id);
                if (freshPhase == null) return;
                _vm.SelectedPhase.Graph = freshPhase.Graph;
                _vm.SelectedPhase.Exits.Clear();
                _vm.SelectedPhase.Exits.AddRange(freshPhase.Exits);

                var layout = AuthoringLayoutRepository.LoadPhaseNodePositions(connection, _vm.SelectedPhase.Id);
                var filled = GraphAutoLayout.FillMissing(_vm.SelectedPhase.Graph.Nodes, _vm.SelectedPhase.Graph.Edges, layout);
                foreach (var pair in filled)
                {
                    if (!LayoutPositionMatches(layout, pair.Key, pair.Value))
                        AuthoringLayoutRepository.SavePhaseNodePosition(connection, _vm.SelectedPhase.Id, pair.Key, pair.Value.X, pair.Value.Y);
                }
                var viewport = AuthoringLayoutRepository.LoadViewport(connection, "phase", _vm.SelectedPhase.Id);
                var phaseId = _vm.SelectedPhase.Id;
                var hydrationGeneration = ++_phaseViewportHydrationGeneration;
                _hydratingPhaseViewport = true;
                _vm.PhaseGraph.LoadFromDefinition(_vm.SelectedPhase, filled, viewport);
                _vm.PhaseGraph.LoadPortalPairs(GraphPortalRepository.LoadPhase(connection, _vm.SelectedPhase.Id));
                ApplyPendingActionSelection(_vm.PhaseGraph);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (hydrationGeneration != _phaseViewportHydrationGeneration) return;
                    if (_vm.SelectedPhase?.Id == phaseId && viewport.HasValue)
                    {
                        _vm.PhaseGraph.ViewportZoom = viewport.Value.Zoom;
                        _vm.PhaseGraph.ViewportLocation = new Point(viewport.Value.X, viewport.Value.Y);
                    }
                    _hydratingPhaseViewport = false;
                }));
            });
        }

        private static bool LayoutPositionMatches(
            Dictionary<string, (double X, double Y)> layout,
            string nodeId,
            (double X, double Y) position)
        {
            return layout.TryGetValue(nodeId, out var saved) &&
                   Math.Abs(saved.X - position.X) < 0.001 &&
                   Math.Abs(saved.Y - position.Y) < 0.001;
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
                _vm.Layout.SessionRatio + _vm.Layout.PhaseRatio,
                _vm.Layout.InspectorRatio,
            });
            var centerRatio = _vm.Layout.SessionRatio + _vm.Layout.PhaseRatio;
            if (centerRatio > 0)
            {
                PaneGrid.RowDefinitions[0].Height = new GridLength(_vm.Layout.SessionRatio / centerRatio, GridUnitType.Star);
                PaneGrid.RowDefinitions[2].Height = new GridLength(_vm.Layout.PhaseRatio / centerRatio, GridUnitType.Star);
            }
        }

        private void SetContentStarWeights(double[] weights)
        {
            var contentColumns = new[]
            {
                PaneGrid.ColumnDefinitions[0],
                PaneGrid.ColumnDefinitions[2],
                PaneGrid.ColumnDefinitions[4],
            };
            for (var i = 0; i < 3; i++)
            {
                contentColumns[i].Width = new GridLength(weights[i], GridUnitType.Star);
            }
        }

        private void OnSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (sender is GridSplitter splitter && splitter.ResizeDirection == GridResizeDirection.Rows)
            {
                PersistCenterRowRatios();
                SaveLayout();
                ApplyLayout();
                return;
            }
            // Convert the dragged pixel layout back to ratios, persist, and
            // re-express as stars so window resizes keep the new proportions.
            PersistCurrentRatios();
            SaveLayout();
            ApplyLayout();
        }

        private void OnInspectorSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            // The inspector split is intentionally local to this window. The
            // existing center-row layout persistence must not overwrite it.
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
            StatusText.Text = "Layout reset to 18/64/18 with stacked graphs";
        }

        private void SaveLayout()
        {
            LayoutPersistence.Save(LayoutPersistence.DefaultSettingsPath(), _vm.Layout);
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            // Dirty card buffer: offer save/discard before losing it.
            if (_cardBuffer != null && _cardBuffer.IsDirty)
            {
                if (!ConfirmLeavingDirtyCard("close the Workbench"))
                {
                    e.Cancel = true;
                    return;
                }
            }

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
            };
            var total = 0.0;
            for (var i = 0; i < 3; i++) total += widths[i];
            if (total <= 0) return;
            var centerRatio = _vm.Layout.SessionRatio + _vm.Layout.PhaseRatio;
            var sessionShare = centerRatio <= 0 ? 0.5 : _vm.Layout.SessionRatio / centerRatio;
            var center = widths[1] / total;
            _vm.Layout.SetRatios(widths[0] / total,
                center * sessionShare,
                center * (1.0 - sessionShare),
                widths[2] / total);
            PersistCenterRowRatios();
        }

        private void PersistCenterRowRatios()
        {
            var sessionHeight = PaneGrid.RowDefinitions[0].ActualHeight;
            var phaseHeight = PaneGrid.RowDefinitions[2].ActualHeight;
            var centerHeight = sessionHeight + phaseHeight;
            var centerRatio = _vm.Layout.SessionRatio + _vm.Layout.PhaseRatio;
            if (centerHeight <= 0 || centerRatio <= 0) return;
            var sessionShare = sessionHeight / centerHeight;
            _vm.Layout.SetRatios(_vm.Layout.LibraryRatio,
                centerRatio * sessionShare,
                centerRatio * (1.0 - sessionShare),
                _vm.Layout.InspectorRatio);
        }

        // ---------- reference player preview (with graph parity) ----------

        private void OnRunSession(object sender, RoutedEventArgs e)
        {
            if (!TryGetRunSeed(out var seed)) return;
            var player = new ReferencePlayerWindow { Owner = this, Seed = seed };
            AttachReferencePlayer(player);
            player.Show();
        }

        private bool TryGetRunSeed(out int seed)
        {
            if (int.TryParse(RunSeedBox.Text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out seed))
            {
                return true;
            }

            StatusText.Text = "Invalid run seed";
            MessageBox.Show(this, "Seed must be a signed 32-bit integer.", "Run seed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void OnRandomizeRunSeed(object sender, RoutedEventArgs e)
        {
            RunSeedBox.Text = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            PushCommand(new CreateSessionCommand(OpenConnection, session.Id, session.Title, session.SessionTypeId));
            _vm.Content.Sessions.Add(session);
            SetLibraryMode(true);
            BindSessionList();
            SessionList.SelectedItem = session;
            StatusText.Text = "Created session '" + session.Title + "' with its singular Start node.";
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

            var layout = WithConnectionResult(connection =>
            {
                var saved = AuthoringLayoutRepository.LoadSessionNodePositions(connection, session.Id);
                return saved.Select(pair => (NodeId: pair.Key, X: pair.Value.X, Y: pair.Value.Y)).ToList();
            });
            PushCommand(new DeleteSessionCommand(OpenConnection, session, layout));
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
            PushCommand(new CopySessionCommand(OpenConnection, clone.Session, layout, clone.EdgeIdMap));

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
            AddPhaseReference(phase, CenterGraphPosition(SessionEditor, _vm.SessionGraph));
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
            var point = location ?? CenterGraphPosition(SessionEditor, _vm.SessionGraph);
            PushCommand(new AddSessionNodeCommand(OpenConnection, _vm.SelectedSession.Id, node,
                new Point2(point.X, point.Y)), reloadSession: true);
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
                var graph = isSession ? (GraphEditorViewModel)_vm.SessionGraph : _vm.PhaseGraph;
                if (graph.PortalEndpoints.Any(endpoint => endpoint.IsSelected))
                {
                    DeleteSelectedPortalIfAny(graph);
                    e.Handled = true;
                    return;
                }
                var node = graph.SelectedNode;
                DeleteSelectedNode(node);
                e.Handled = true;
            }
        }

        private void DeleteSelectedNode(GraphNodeViewModel node)
        {
            if (node == null) return;
            if (_vm.SessionGraph.Nodes.Contains(node))
            {
                if (node.Kind == "start")
                {
                    StatusText.Text = "The Start node cannot be deleted (singular).";
                    return;
                }
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
                CompareValue = 100f,
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
            var point = location ?? CenterGraphPosition(PhaseEditor, _vm.PhaseGraph);
            PushCommand(new AddPhaseNodeCommand(OpenConnection, _vm.SelectedPhase.Id, node,
                new Point2(point.X, point.Y)), reloadPhase: true);
        }

        /// <summary>
        /// Converts the visible center of a Nodify canvas to graph coordinates.
        /// Nodify exposes ViewportLocation as the viewport's graph-space
        /// top-left, so the visible center is the top-left plus half the
        /// viewport size. Deliberate overlap is acceptable: the user can move
        /// newly inserted nodes apart after creating them.
        /// </summary>
        private static Point CenterGraphPosition(FrameworkElement editor, GraphEditorViewModel graph)
        {
            var width = editor != null && editor.ActualWidth > 1 ? editor.ActualWidth : 600;
            var height = editor != null && editor.ActualHeight > 1 ? editor.ActualHeight : 300;
            var zoom = graph?.ViewportZoom > 0.0001 ? graph.ViewportZoom : 1.0;
            var viewport = graph?.ViewportLocation ?? new Point();
            return new Point(
                viewport.X + (width * 0.5 / zoom) - 105,
                viewport.Y + (height * 0.5 / zoom) - 65);
        }

        // ---------- phase library CRUD + header ----------

        private void OnNewPhase(object sender, RoutedEventArgs e)
        {
            var phase = new PhaseDefinition
            {
                Id = "phase-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Title = "New Phase",
            };
            PushCommand(new CreatePhaseCommand(OpenConnection, phase.Id, phase.Title));
            _vm.Content.Phases.Add(phase);
            SetLibraryMode(false);
            BindPhaseList();
            PhaseList.SelectedItem = phase;
            StatusText.Text = "Created phase '" + phase.Title + "' with its singular Entry node.";
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

            var layout = WithConnectionResult(connection =>
            {
                var saved = AuthoringLayoutRepository.LoadPhaseNodePositions(connection, phase.Id);
                return saved.Select(pair => (NodeId: pair.Key, X: pair.Value.X, Y: pair.Value.Y)).ToList();
            });
            PushCommand(new DeletePhaseCommand(OpenConnection, phase, layout));
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
            PushCommand(new DuplicatePhaseCommand(OpenConnection, clone.Phase, layout, clone.EdgeIdMap));

            _vm.Content.Phases.Add(clone.Phase);
            BindPhaseList();
            PhaseList.SelectedItem = clone.Phase;
            StatusText.Text = "Duplicated phase — exits, graph, action instances and layout deep-cloned.";
        }

        private void OnShowSessions(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) return;
            _library.FilterToSessionsUsing(_vm.SelectedPhase);
            SessionModeText.Text = "Sessions using " + _vm.SelectedPhase.Title;
            SetLibraryMode(true);
            BindSessionList();
            if (SessionList.Items.Count > 0) SessionList.SelectedIndex = 0;
            StatusText.Text = "Library filtered to sessions using '" + _vm.SelectedPhase.Title + "'.";
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

        private void OnPhaseListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (!(PhaseList.SelectedItem is PhaseDefinition phase)) return;
            var data = new DataObject("TruthCardGame.PhaseId", phase.Id);
            DragDrop.DoDragDrop(PhaseList, data, DragDropEffects.Copy);
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
