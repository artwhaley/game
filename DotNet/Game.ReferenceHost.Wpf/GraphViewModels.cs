using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Linq;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Simple ICommand delegating to an Action.</summary>
    public sealed class DelegateCommand<T> : ICommand
    {
        private readonly Action<T> _action;
        private readonly Func<T, bool> _condition;

        public DelegateCommand(Action<T> action, Func<T, bool> condition = null)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _condition = condition;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            if (parameter is T value) return _condition?.Invoke(value) ?? true;
            return _condition?.Invoke(default) ?? true;
        }

        public void Execute(object parameter)
        {
            if (parameter is T value) _action(value);
            else _action(default);
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One graph node on the canvas (Nodify ItemContainer data context).</summary>
    public sealed class GraphNodeViewModel : INotifyPropertyChanged
    {
        private Point _location;
        private bool _isSelected;

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Kind { get; set; } = "";

        /// <summary>Domain reference for the node kind (e.g. the referenced phase id of a PhaseReference).</summary>
        public string RefId { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        /// <summary>Input connectors (left side). Nodify's Node control binds its Input template to this collection.</summary>
        public ObservableCollection<ConnectorViewModel> Inputs { get; } = new ObservableCollection<ConnectorViewModel>();

        /// <summary>Output connectors (right side).</summary>
        public ObservableCollection<ConnectorViewModel> Outputs { get; } = new ObservableCollection<ConnectorViewModel>();

        public Point Location
        {
            get => _location;
            set
            {
                if (_location != value)
                {
                    _location = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
                }
            }
        }

        /// <summary>Inline VariableCheck editor data (null for non-check nodes).</summary>
        public VariableCheckData Check { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Editable fields of an inline VariableCheck node. Persisted through the
    /// owning editor's CheckChanged event when any field changes.
    /// </summary>
    public sealed class VariableCheckData : INotifyPropertyChanged
    {
        private string _source = "progress";
        private string _op = ">=";
        private string _valueText = "0";
        private string _key = "";

        /// <summary>Temperature id / stat key for non-progress sources; preserved on edit.</summary>
        public string Key
        {
            get => _key;
            set
            {
                if (_key != value)
                {
                    _key = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Key)));
                }
            }
        }

        public string Source
        {
            get => _source;
            set
            {
                if (_source != value)
                {
                    _source = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
                }
            }
        }

        public string Operator
        {
            get => _op;
            set
            {
                if (_op != value)
                {
                    _op = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Operator)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
                }
            }
        }

        public string ValueText
        {
            get => _valueText;
            set
            {
                if (_valueText != value)
                {
                    _valueText = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
                }
            }
        }

        public string Summary => Source + " " + Operator + " " + ValueText;

        public static readonly string[] Sources = { "progress", "temperature", "stat" };
        public static readonly string[] Operators = { "<", "<=", "==", "!=", ">=", ">" };

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>One connector socket on a node. Anchor is written by Nodify as the node moves (OneWayToSource binding).</summary>
    public sealed class ConnectorViewModel : INotifyPropertyChanged
    {
        private Point _anchor;
        private bool _isConnected;

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Kind { get; set; } = "";      // normal / phase_exit / session_goto / true / false
        public string Tag { get; set; } = "";       // phase exit id / session goto instance id

        /// <summary>The node that owns this connector (edge persistence resolves the target node id).</summary>
        public GraphNodeViewModel Owner { get; set; }

        public Point Anchor
        {
            get => _anchor;
            set
            {
                if (_anchor != value)
                {
                    _anchor = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Anchor)));
                }
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>One directed edge between a source output and a target node's input.</summary>
    public sealed class ConnectionViewModel
    {
        public ConnectionViewModel(ConnectorViewModel source, ConnectorViewModel target)
        {
            Source = source;
            Target = target;
            Source.IsConnected = true;
            Target.IsConnected = true;
        }

        public ConnectorViewModel Source { get; }
        public ConnectorViewModel Target { get; }
    }

    /// <summary>In-flight connector drag; Nodify drives StartedCommand/CompletedCommand.</summary>
    public sealed class PendingConnectionViewModel
    {
        private readonly GraphEditorViewModel _editor;
        private ConnectorViewModel _source;

        public PendingConnectionViewModel(GraphEditorViewModel editor)
        {
            _editor = editor;
            StartCommand = new DelegateCommand<ConnectorViewModel>(source => _source = source);
            CompletedCommand = new DelegateCommand<ConnectorViewModel>(target =>
            {
                if (target != null && _source != null && _source != target)
                {
                    _editor.Connect(_source, target);
                }
                _source = null;
            });
        }

        public ICommand StartCommand { get; }
        public ICommand CompletedCommand { get; }
    }

    /// <summary>Shared Nodify editor state: nodes, connections, viewport, pending connection, selection.</summary>
    public abstract class GraphEditorViewModel
    {
        public ObservableCollection<GraphNodeViewModel> Nodes { get; } = new ObservableCollection<GraphNodeViewModel>();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new ObservableCollection<ConnectionViewModel>();

        public PendingConnectionViewModel PendingConnection { get; }
        public ICommand DisconnectConnectorCommand { get; }

        private Point _viewportLocation;
        private double _viewportZoom = 1.0;

        /// <summary>Pan offset; MainWindow persists it via ViewportChanged.</summary>
        public Point ViewportLocation
        {
            get => _viewportLocation;
            set
            {
                if (_viewportLocation != value)
                {
                    _viewportLocation = value;
                    ViewportChanged?.Invoke();
                }
            }
        }

        /// <summary>Zoom; MainWindow persists it via ViewportChanged.</summary>
        public double ViewportZoom
        {
            get => _viewportZoom;
            set
            {
                if (Math.Abs(_viewportZoom - value) > 0.0001)
                {
                    _viewportZoom = value;
                    ViewportChanged?.Invoke();
                }
            }
        }

        /// <summary>Selected node, for the Inspector pane.</summary>
        public GraphNodeViewModel SelectedNode { get; private set; }

        public event EventHandler SelectionChanged;

        /// <summary>A node's Location changed (persist the layout row).</summary>
        public event Action<GraphNodeViewModel> NodeMoved;

        /// <summary>A directed edge was just created in the editor (persist it).</summary>
        public event Action<ConnectorViewModel, ConnectorViewModel> ConnectionCreated;

        /// <summary>An edge was just removed in the editor (persist the removal).</summary>
        public event Action<ConnectionViewModel> ConnectionRemoved;

        /// <summary>A node was deleted from the editor (persist node + cascade).</summary>
        public event Action<GraphNodeViewModel> NodeDeleted;

        /// <summary>An inline VariableCheck field changed (persist the comparison).</summary>
        public event Action<GraphNodeViewModel> CheckChanged;

        /// <summary>Pan or zoom changed (persist the viewport row).</summary>
        public event Action ViewportChanged;

        protected GraphEditorViewModel()
        {
            PendingConnection = new PendingConnectionViewModel(this);
            DisconnectConnectorCommand = new DelegateCommand<ConnectorViewModel>(connector =>
            {
                for (var i = Connections.Count - 1; i >= 0; i--)
                {
                    var connection = Connections[i];
                    if (connection.Source == connector || connection.Target == connector)
                    {
                        connection.Source.IsConnected = false;
                        connection.Target.IsConnected = false;
                        Connections.RemoveAt(i);
                        ConnectionRemoved?.Invoke(connection);
                    }
                }
            });
        }

        /// <summary>Builds the node/connection graph from content definitions.</summary>
        public void Populate(IEnumerable<GraphNodeViewModel> nodes, IEnumerable<ConnectionViewModel> connections)
        {
            SelectedNode = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Nodes.Clear();
            Connections.Clear();
            foreach (var node in nodes)
            {
                node.PropertyChanged += OnNodePropertyChanged;
                if (node.Check != null)
                {
                    node.Check.PropertyChanged += (_, _) => CheckChanged?.Invoke(node);
                }
                Nodes.Add(node);
            }
            foreach (var connection in connections) Connections.Add(connection);
        }

        private void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var node = (GraphNodeViewModel)sender;
            if (e.PropertyName == nameof(GraphNodeViewModel.Location))
            {
                NodeMoved?.Invoke(node);
                return;
            }
            if (e.PropertyName != nameof(GraphNodeViewModel.IsSelected)) return;
            if (node.IsSelected && SelectedNode != node)
            {
                if (SelectedNode != null) SelectedNode.IsSelected = false;
                SelectedNode = node;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (!node.IsSelected && SelectedNode == node)
            {
                SelectedNode = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Adds a raw (unsaved) node; returns it. Palette + future authoring call this.</summary>
        public GraphNodeViewModel AddNode(string id, string title, string subtitle, string kind, Point location)
        {
            var node = new GraphNodeViewModel
            {
                Id = id,
                Title = title,
                Subtitle = subtitle,
                Kind = kind,
                Location = location,
            };
            var input = new ConnectorViewModel { Id = id + "-input", Title = "" };
            input.Owner = node;
            node.Inputs.Add(input);
            node.PropertyChanged += OnNodePropertyChanged;
            Nodes.Add(node);
            return node;
        }

        /// <summary>Connects a source output to a target node input (live connect in the editor).</summary>
        public void Connect(ConnectorViewModel source, ConnectorViewModel target)
        {
            if (source == null || target == null) return;
            Connections.Add(new ConnectionViewModel(source, target));
            ConnectionCreated?.Invoke(source, target);
        }

        /// <summary>
        /// Removes a node and every edge touching it, raising the authoring
        /// events so the host can persist the deletion. Keeps the editor's
        /// selection consistent.
        /// </summary>
        public void DeleteNode(GraphNodeViewModel node)
        {
            if (node == null || !Nodes.Contains(node)) return;

            for (var i = Connections.Count - 1; i >= 0; i--)
            {
                var connection = Connections[i];
                if (connection.Source == null || connection.Target == null) continue;
                var touches = false;
                foreach (var output in node.Outputs)
                {
                    if (connection.Source == output) touches = true;
                }
                foreach (var input in node.Inputs)
                {
                    if (connection.Target == input) touches = true;
                }
                if (!touches) continue;
                connection.Source.IsConnected = false;
                connection.Target.IsConnected = false;
                Connections.RemoveAt(i);
                ConnectionRemoved?.Invoke(connection);
            }

            node.PropertyChanged -= OnNodePropertyChanged;
            Nodes.Remove(node);
            if (SelectedNode == node)
            {
                SelectedNode = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            NodeDeleted?.Invoke(node);
        }

        public void ClearSelection()
        {
            if (SelectedNode != null) SelectedNode.IsSelected = false;
        }

        /// <summary>Adds an output connector to a node (public for palette use).</summary>
        public static ConnectorViewModel Output(GraphNodeViewModel node, string id, string title, string kind, string tag)
        {
            var output = new ConnectorViewModel { Id = id, Title = title, Kind = kind, Tag = tag };
            output.Owner = node;
            node.Outputs.Add(output);
            return output;
        }
    }

    /// <summary>Session graph editor: Start / PhaseReference / SessionDecision / End.</summary>
    public sealed class SessionGraphViewModel : GraphEditorViewModel
    {
        private SessionDefinition _loaded;

        /// <summary>
        /// Resolves a referenced phase id to its definition (for readable node
        /// titles + exit summaries). Set by the shell from the loaded content.
        /// </summary>
        public Func<string, PhaseDefinition> PhaseResolver { get; set; }

        /// <summary>The session being authored (null until loaded).</summary>
        public SessionDefinition LoadedSession => _loaded;

        public void LoadFromDefinition(SessionDefinition session, Dictionary<string, (double X, double Y)> layout = null, (double Zoom, double X, double Y)? viewport = null)
        {
            _loaded = session;
            if (session?.Graph == null)
            {
                Populate(new GraphNodeViewModel[0], new ConnectionViewModel[0]);
                return;
            }

            var nodes = new Dictionary<string, GraphNodeViewModel>();
            var connections = new List<ConnectionViewModel>();

            // Layout: persisted coordinates when present, otherwise a left-to-right
            // grid by graph order, stacked vertically with overlap avoided.
            var column = 0.0;
            var row = 0.0;
            foreach (var node in session.Graph.Nodes)
            {
                var (title, subtitle, kind, refId) = DescribeSessionNode(node);
                var point = layout != null && layout.TryGetValue(node.Id, out var saved)
                    ? new Point(saved.X, saved.Y)
                    : new Point(column * 260, row * 160);
                var vm = new GraphNodeViewModel
                {
                    Id = node.Id,
                    Title = title,
                    Subtitle = subtitle,
                    Kind = kind,
                    RefId = refId,
                    Location = point,
                };
                var input = new ConnectorViewModel { Id = node.Id + "-input", Title = "" };
                input.Owner = vm;
                vm.Inputs.Add(input);
                nodes[node.Id] = vm;
                column++;
                if (column >= 5) { column = 0; row++; }
            }

            var outputsByNode = new Dictionary<string, ConnectorViewModel>();
            foreach (var node in session.Graph.Nodes)
            {
                var vm = nodes[node.Id];
                var phaseRefId = (node as PhaseReferenceNodeDefinition)?.PhaseId;
                foreach (var output in node.Outputs)
                {
                    var (label, kind, tag) = DescribeSessionOutput(output, phaseRefId);
                    outputsByNode[output.Id] = Output(vm, output.Id, label, kind, tag);
                }
            }

            foreach (var edge in session.Graph.Edges)
            {
                if (!outputsByNode.TryGetValue(edge.SourceOutputId, out var source)) continue;
                if (!nodes.TryGetValue(edge.TargetNodeId, out var target)) continue;
                connections.Add(new ConnectionViewModel(source, target.Inputs[0]));
            }

            Populate(nodes.Values, connections);
            if (viewport.HasValue)
            {
                ViewportZoom = viewport.Value.Zoom;
                ViewportLocation = new Point(viewport.Value.X, viewport.Value.Y);
            }
        }

        /// <summary>Palette: adds a PhaseReference targeting the given phase id (title resolved for readable display).</summary>
        public GraphNodeViewModel AddPhaseReference(string phaseId, Point location)
        {
            var id = "phase-ref-" + (Nodes.Count + 1);
            PhaseDefinition phase = null;
            try
            {
                if (PhaseResolver != null) phase = PhaseResolver(phaseId);
            }
            catch
            {
                // Unknown reference; fall back to the id.
            }
            var title = $"Phase: {phase?.Title ?? phaseId}";
            var subtitle = phase?.Exits != null && phase.Exits.Count > 0
                ? $"{phase.Exits.Count} exit{(phase.Exits.Count == 1 ? "" : "s")}: {string.Join(", ", phase.Exits.Select(e => string.IsNullOrEmpty(e.Name) ? e.Id : e.Name))}"
                : "no exits";
            var node = AddNode(id, title, subtitle, "phase-reference", location);
            node.RefId = phaseId;
            Output(node, id + "-out", "out", "normal", "");
            return node;
        }

        /// <summary>Palette: adds a SessionDecision node.</summary>
        public GraphNodeViewModel AddSessionDecision(Point location)
        {
            var id = "decision-" + (Nodes.Count + 1);
            var node = AddNode(id, "Session Decision", "prompt…", "decision", location);
            Output(node, id + "-out", "out", "normal", "");
            return node;
        }

        /// <summary>Palette: adds a SessionEnd node.</summary>
        public GraphNodeViewModel AddSessionEnd(Point location)
        {
            var id = "end-" + (Nodes.Count + 1);
            var node = AddNode(id, "End", "Session terminal", "end", location);
            return node;
        }

        private (string, string, string, string) DescribeSessionNode(SessionGraphNodeDefinition node)
        {
            switch (node)
            {
                case SessionStartNodeDefinition:
                    return ("Start", "Session entry", "start", "");
                case PhaseReferenceNodeDefinition reference:
                    return DescribePhaseReference(reference);
                case SessionDecisionNodeDefinition decision:
                    return ("Session Decision", decision.Prompt, "decision", "");
                case SessionEndNodeDefinition:
                    return ("End", "Session terminal", "end", "");
                default:
                    return (node.GetType().Name, node.Id, "unknown", "");
            }
        }

        private (string, string, string, string) DescribePhaseReference(PhaseReferenceNodeDefinition reference)
        {
            PhaseDefinition phase = null;
            try
            {
                if (PhaseResolver != null) phase = PhaseResolver(reference.PhaseId);
            }
            catch
            {
                // Unknown reference: fall back to the raw id below.
            }

            var title = phase?.Title ?? reference.PhaseId;
            var exits = phase?.Exits ?? new List<PhaseExitDefinition>();
            var subtitle = exits.Count > 0
                ? $"{exits.Count} exit{(exits.Count == 1 ? "" : "s")}: {string.Join(", ", exits.Select(e => string.IsNullOrEmpty(e.Name) ? e.Id : e.Name))}"
                : "no exits";
            return ($"Phase: {title}", subtitle, "phase-reference", reference.PhaseId);
        }

        private (string, string, string) DescribeSessionOutput(GraphOutputDefinition output, string phaseRefId)
        {
            switch (output.Kind)
            {
                case GraphPortKind.Normal:
                    return ("out", "normal", "");
                case GraphPortKind.PhaseExit:
                {
                    var label = output.Label;
                    if (string.IsNullOrEmpty(label))
                    {
                        label = ExitLabel(output.PhaseExitId, phaseRefId) ?? "exit";
                    }
                    return (label, "phase_exit", output.PhaseExitId);
                }
                case GraphPortKind.SessionGoto:
                    return (string.IsNullOrEmpty(output.Label) ? "goto" : output.Label, "session_goto", output.SessionGotoActionInstanceId);
                default:
                    return (output.Kind.ToString(), "normal", "");
            }
        }

        /// <summary>Resolves an exit id to its author-facing name ("Complete" / "Fail") via the referenced phase.</summary>
        private string ExitLabel(string exitId, string phaseRefId)
        {
            if (string.IsNullOrEmpty(exitId)) return null;
            PhaseDefinition phase = null;
            try { if (PhaseResolver != null && !string.IsNullOrEmpty(phaseRefId)) phase = PhaseResolver(phaseRefId); }
            catch { return null; }
            if (phase?.Exits != null)
            {
                foreach (var exit in phase.Exits)
                {
                    if (exit.Id == exitId) return string.IsNullOrEmpty(exit.Name) ? exitId : exit.Name;
                }
            }
            return exitId;
        }
    }

    /// <summary>Phase graph editor: Entry / CardExecutor / VariableCheck / ActionNode / Decision / Return.</summary>
    public sealed class PhaseGraphViewModel : GraphEditorViewModel
    {
        private PhaseDefinition _loaded;

        /// <summary>The phase being authored (null until loaded).</summary>
        public PhaseDefinition LoadedPhase => _loaded;

        /// <summary>
        /// True when this phase was opened from a PhaseReference placement in the
        /// Session canvas (Ticket 14). Enables placement-scoped actions like
        /// Make Unique (Ticket 17); false when selected directly in the Library.
        /// </summary>
        public bool HasPlacementContext { get; set; }

        public void LoadFromDefinition(PhaseDefinition phase, Dictionary<string, (double X, double Y)> layout = null, (double Zoom, double X, double Y)? viewport = null)
        {
            _loaded = phase;
            if (phase?.Graph == null)
            {
                Populate(new GraphNodeViewModel[0], new ConnectionViewModel[0]);
                return;
            }

            var nodes = new Dictionary<string, GraphNodeViewModel>();
            var connections = new List<ConnectionViewModel>();

            // Exit lookup for readable PhaseGoto Action node titles.
            var exitNameById = new Dictionary<string, string>();
            foreach (var exit in phase.Exits ?? new List<PhaseExitDefinition>())
            {
                exitNameById[exit.Id] = string.IsNullOrEmpty(exit.Name) ? exit.Id : exit.Name;
            }

            var column = 0.0;
            var row = 0.0;
            foreach (var node in phase.Graph.Nodes)
            {
                var (title, subtitle, kind) = DescribePhaseNode(node, exitNameById);
                var point = layout != null && layout.TryGetValue(node.Id, out var saved)
                    ? new Point(saved.X, saved.Y)
                    : new Point(column * 220, row * 150);
                var vm = new GraphNodeViewModel
                {
                    Id = node.Id,
                    Title = title,
                    Subtitle = subtitle,
                    Kind = kind,
                    Location = point,
                };
                if (node is VariableCheckNodeDefinition check)
                {
                    vm.Check = new VariableCheckData
                    {
                        Source = SourceKindName(check.SourceKind),
                        Operator = OperatorName(check.Operator),
                        ValueText = check.CompareValue.ToString("0.#"),
                        Key = check.VariableKey ?? "",
                    };
                }
                var input = new ConnectorViewModel { Id = node.Id + "-input", Title = "" };
                input.Owner = vm;
                vm.Inputs.Add(input);
                nodes[node.Id] = vm;
                column++;
                if (column >= 6) { column = 0; row++; }
            }

            var outputsByNode = new Dictionary<string, ConnectorViewModel>();
            foreach (var node in phase.Graph.Nodes)
            {
                var vm = nodes[node.Id];
                foreach (var output in node.Outputs)
                {
                    var (label, kind, tag) = DescribePhaseOutput(output);
                    outputsByNode[output.Id] = Output(vm, output.Id, label, kind, tag);
                }
            }

            foreach (var edge in phase.Graph.Edges)
            {
                if (!outputsByNode.TryGetValue(edge.SourceOutputId, out var source)) continue;
                if (!nodes.TryGetValue(edge.TargetNodeId, out var target)) continue;
                connections.Add(new ConnectionViewModel(source, target.Inputs[0]));
            }

            Populate(nodes.Values, connections);
            if (viewport.HasValue)
            {
                ViewportZoom = viewport.Value.Zoom;
                ViewportLocation = new Point(viewport.Value.X, viewport.Value.Y);
            }
        }

        /// <summary>Palette: adds a CardExecutor node.</summary>
        public GraphNodeViewModel AddCardExecutor(Point location)
        {
            var id = "card-" + (Nodes.Count + 1);
            var node = AddNode(id, "Draw Card", "", "card", location);
            Output(node, id + "-out", "out", "normal", "");
            return node;
        }

        /// <summary>Palette: adds a VariableCheck node.</summary>
        public GraphNodeViewModel AddVariableCheck(Point location)
        {
            var id = "check-" + (Nodes.Count + 1);
            var node = AddNode(id, "Check", "progress ≥ target", "check", location);
            Output(node, id + "-true", "true", "true", "");
            Output(node, id + "-false", "false", "false", "");
            return node;
        }

        /// <summary>Palette: adds an ActionNode.</summary>
        public GraphNodeViewModel AddActionNode(Point location)
        {
            var id = "action-" + (Nodes.Count + 1);
            var node = AddNode(id, "Action", "(action type)", "action", location);
            Output(node, id + "-out", "out", "normal", "");
            return node;
        }

        /// <summary>Palette: adds a PhaseDecision node.</summary>
        public GraphNodeViewModel AddPhaseDecision(Point location)
        {
            var id = "decision-" + (Nodes.Count + 1);
            var node = AddNode(id, "Decision", "prompt…", "decision", location);
            Output(node, id + "-out", "out", "normal", "");
            return node;
        }

        /// <summary>Palette: adds a Return node.</summary>
        public GraphNodeViewModel AddReturn(Point location)
        {
            var id = "return-" + (Nodes.Count + 1);
            var node = AddNode(id, "Return", "", "return", location);
            return node;
        }

        private static string SourceKindName(VariableSourceKind kind)
        {
            switch (kind)
            {
                case VariableSourceKind.PhaseProgress: return "progress";
                case VariableSourceKind.Temperature: return "temperature";
                case VariableSourceKind.Stat: return "stat";
                default: return "progress";
            }
        }

        private static string OperatorName(VariableCompareOperator op)
        {
            switch (op)
            {
                case VariableCompareOperator.LessThan: return "<";
                case VariableCompareOperator.LessThanOrEqual: return "<=";
                case VariableCompareOperator.Equal: return "==";
                case VariableCompareOperator.NotEqual: return "!=";
                case VariableCompareOperator.GreaterThanOrEqual: return ">=";
                case VariableCompareOperator.GreaterThan: return ">";
                default: return ">=";
            }
        }

        private static (string, string, string) DescribePhaseNode(GraphNodeDefinition node, Dictionary<string, string> exitNameById)
        {
            switch (node)
            {
                case PhaseEntryNodeDefinition:
                    return ("Entry", "", "entry");
                case CardExecutorNodeDefinition:
                    return ("Draw Card", "", "card");
                case VariableCheckNodeDefinition check:
                    return ("Check", $"{check.SourceKind} {check.Operator} {check.CompareValue}", "check");
                case ActionNodeDefinition action:
                    return DescribeActionNode(action, exitNameById);
                case PhaseDecisionNodeDefinition decision:
                    return ("Decision", decision.Prompt, "decision");
                case ReturnNodeDefinition:
                    return ("Return", "", "return");
                default:
                    return (node.GetType().Name, node.Id, "unknown");
            }
        }

        /// <summary>
        /// Names an Action node by its flow effect: a PhaseGoto becomes
        /// "Exit: Complete / Fail" instead of a raw uuid. Other action types
        /// keep a generic title.
        /// </summary>
        private static (string, string, string) DescribeActionNode(ActionNodeDefinition action, Dictionary<string, string> exitNameById)
        {
            var exportsExit = false;
            string exitName = null;
            if (action.Sequence?.Instances != null)
            {
                foreach (var instance in action.Sequence.Instances)
                {
                    if (instance is PhaseGotoInstanceDefinition gotoInstance)
                    {
                        exportsExit = true;
                        exitName = exitNameById.TryGetValue(gotoInstance.PhaseExitId, out var name)
                            ? name
                            : (string.IsNullOrEmpty(gotoInstance.PhaseExitId) ? "" : gotoInstance.PhaseExitId);
                        break;
                    }
                }
            }
            if (exportsExit)
            {
                return ($"Exit: {exitName}", "phase_goto", "action");
            }
            return ("Action", action.Id, "action");
        }

        private static (string, string, string) DescribePhaseOutput(GraphOutputDefinition output)
        {
            switch (output.Kind)
            {
                case GraphPortKind.Normal:
                    return ("out", "normal", "");
                case GraphPortKind.True:
                    return ("true", "true", "");
                case GraphPortKind.False:
                    return ("false", "false", "");
                default:
                    return (output.Kind.ToString(), "normal", "");
            }
        }
    }

    /// <summary>Shell-level view model: content + the four pane editors + layout.</summary>
    public sealed class WorkbenchViewModel : INotifyPropertyChanged
    {
        public SessionGraphViewModel SessionGraph { get; } = new SessionGraphViewModel();
        public PhaseGraphViewModel PhaseGraph { get; } = new PhaseGraphViewModel();
        public WorkbenchLayout Layout { get; } = new WorkbenchLayout();

        public GameContentDefinition Content { get; private set; }
        public SessionDefinition SelectedSession { get; private set; }
        public PhaseDefinition SelectedPhase { get; private set; }

        private GraphNodeViewModel _selectedNode;

        /// <summary>Aggregate selection across both editors, for the Inspector pane.</summary>
        public GraphNodeViewModel SelectedNode
        {
            get => _selectedNode;
            private set
            {
                if (_selectedNode != value)
                {
                    _selectedNode = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNode)));
                }
            }
        }

        public WorkbenchViewModel()
        {
            SessionGraph.SelectionChanged += (_, _) =>
            {
                if (SessionGraph.SelectedNode != null) SelectedNode = SessionGraph.SelectedNode;
            };
            PhaseGraph.SelectionChanged += (_, _) =>
            {
                if (PhaseGraph.SelectedNode != null) SelectedNode = PhaseGraph.SelectedNode;
            };
        }

        public void LoadContent(GameContentDefinition content)
        {
            Content = content;
            var phasesById = content.Phases.ToDictionary(p => p.Id);
            SessionGraph.PhaseResolver = id =>
                id != null && phasesById.TryGetValue(id, out var phase) ? phase : null;
            SelectSession(content.Sessions.Count > 0 ? content.Sessions[0] : null);
            SelectPhase(content.Phases.Count > 0 ? content.Phases[0] : null);
        }

        /// <summary>Loads a phase by id into the Phase Graph pane; false when unknown.</summary>
        public bool SelectPhaseById(string phaseId)
        {
            if (Content == null || string.IsNullOrEmpty(phaseId)) return false;
            foreach (var phase in Content.Phases)
            {
                if (phase.Id == phaseId)
                {
                    SelectPhase(phase);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Loads a session into the Session Graph pane (Library selection).</summary>
        public void SelectSession(SessionDefinition session)
        {
            SelectedSession = session;
            SessionGraph.LoadFromDefinition(session);
        }

        /// <summary>Loads a phase into the Phase Graph pane (Library selection).</summary>
        public void SelectPhase(PhaseDefinition phase)
        {
            SelectedPhase = phase;
            PhaseGraph.LoadFromDefinition(phase);
        }

        /// <summary>True when a session graph contains a Start node (palette disables the singular Start action).</summary>
        public bool SessionHasStart()
        {
            foreach (var node in SessionGraph.Nodes)
            {
                if (node.Kind == "start") return true;
            }
            return false;
        }

        /// <summary>True when the phase graph contains an Entry node (singular palette action).</summary>
        public bool PhaseHasEntry()
        {
            foreach (var node in PhaseGraph.Nodes)
            {
                if (node.Kind == "entry") return true;
            }
            return false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
