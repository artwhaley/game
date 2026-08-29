using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
        private bool _debugActive;
        private string _decisionPrompt = "";

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

        /// <summary>Ordered explicit Action Instance rows owned by this node.</summary>
        public ObservableCollection<ActionRowData> ActionRows { get; } = new ObservableCollection<ActionRowData>();

        /// <summary>Shared sequence editor for an Action node; decision options use the same type.</summary>
        public ActionSequenceEditorViewModel ActionSequence { get; set; }

        private string _selectedActionTypeKey;
        public string SelectedActionTypeKey
        {
            get => _selectedActionTypeKey;
            set
            {
                if (_selectedActionTypeKey != value)
                {
                    _selectedActionTypeKey = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedActionTypeKey)));
                }
            }
        }

        public bool CanEditActions { get; set; }

        /// <summary>
        /// Decision-node editing: prompt text + 1-3 option rows (each with its own
        /// action sequence). Scope is "session" (SessionGoto) or "phase" (PhaseGoto).
        /// </summary>
        public string DecisionScope { get; set; }

        public string DecisionPrompt
        {
            get => _decisionPrompt;
            set
            {
                if (_decisionPrompt != value)
                {
                    _decisionPrompt = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DecisionPrompt)));
                }
            }
        }

        public ObservableCollection<DecisionRowData> DecisionRows { get; } = new ObservableCollection<DecisionRowData>();

        public bool CanAddOption { get; set; }

        /// <summary>Live-debug highlight (Ticket 19): true while the Core VM is at this node.</summary>
        public bool DebugActive
        {
            get => _debugActive;
            set
            {
                if (_debugActive != value)
                {
                    _debugActive = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DebugActive)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>One selectable Phase exit in the GOTO row ComboBox.</summary>
    public sealed class ExitOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
    }

    /// <summary>One PhaseExit chip in the Phase-pane exits strip.</summary>
    public sealed class ExitRowViewModel : INotifyPropertyChanged
    {
        private string _name;

        public string Id { get; set; }

        public bool CanDelete { get; set; }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Explicit, instance-local action editor row; fields map to one typed subtype.</summary>
    public sealed class ActionRowData : INotifyPropertyChanged
    {
        private string _textValue;
        private string _numberText;
        private string _secondaryNumberText;
        private bool _isExpanded = true;

        public GraphNodeViewModel Owner { get; set; }
        public ActionSequenceEditorViewModel Sequence { get; set; }
        public ActionInstanceDefinition Definition { get; set; }
        public string SequenceId { get; set; }
        public int Ordinal { get; set; }
        public string InstanceId => Definition?.Id;
        public string TypeKey { get; set; }
        public string DisplayLabel { get; set; }
        public ObservableCollection<PromptChoiceOptionRowData> PromptOptions { get; } = new ObservableCollection<PromptChoiceOptionRowData>();
        public bool IsPromptChoice => Definition is PromptChoiceInstanceDefinition;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandLabel)));
            }
        }
        public string ExpandLabel => IsExpanded ? "Collapse" : "Expand";
        public List<ExitOption> ExitOptions { get; set; } = new List<ExitOption>();
        public List<ActionParameterOption> ParameterOptions { get; set; } = new List<ActionParameterOption>();
        public IEnumerable<ActionParameterOption> ChoiceOptions
        {
            get
            {
                if (TypeKey == ActionTypeKeys.PhaseGoto)
                    return ExitOptions.Select(option => new ActionParameterOption { Id = option.Id, Name = option.Name });
                return ParameterOptions;
            }
        }
        public string PersistedTextValue { get; set; }
        public string PersistedNumberText { get; set; }
        public string PersistedSecondaryNumberText { get; set; }

        public ActionEditorDescriptor Editor => ActionEditorRegistry.For(TypeKey);
        public bool HasTextEditor => Editor.HasTextEditor;
        public bool HasNumberEditor => Editor.HasNumberEditor;
        public bool HasSecondaryNumberEditor => Editor.HasSecondaryNumberEditor;
        public bool HasChoiceEditor => Editor.HasChoiceEditor;
        public bool HasStrictChoiceEditor => Editor.HasChoiceEditor && !Editor.IsChoiceEditable;
        public bool HasEditableChoiceEditor => Editor.HasChoiceEditor && Editor.IsChoiceEditable;
        public bool IsDanger => !string.IsNullOrEmpty(ValidationMessage);

        public string ValidationMessage
        {
            get
            {
                if (Editor.HasNumberEditor && !string.IsNullOrWhiteSpace(NumberText) &&
                    !float.TryParse(NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return "Enter a number.";
                if (Editor.HasSecondaryNumberEditor && !string.IsNullOrWhiteSpace(SecondaryNumberText) &&
                    !float.TryParse(SecondaryNumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return "Enter a number.";
                if (TypeKey == ActionTypeKeys.PhaseGoto && string.IsNullOrWhiteSpace(TextValue))
                    return "Assign a PhaseExit before playback.";
                if (TypeKey == ActionTypeKeys.StatIncrease && string.IsNullOrWhiteSpace(TextValue))
                    return "Select or enter a Stat.";
                if (TypeKey == ActionTypeKeys.ModifyTemperature && string.IsNullOrWhiteSpace(TextValue))
                    return "Select a Temperature.";
                if (TypeKey == ActionTypeKeys.Cutscene &&
                    (string.IsNullOrWhiteSpace(TextValue) || !ParameterOptions.Any(option => option.Id == TextValue)))
                    return "Select an existing Resource.";
                if (TypeKey == ActionTypeKeys.ToyActivity &&
                    (string.IsNullOrWhiteSpace(TextValue) || !ParameterOptions.Any(option => option.Id == TextValue)))
                    return "Select an existing Toy Capability.";
                return null;
            }
        }

        public string TextValue
        {
            get => _textValue;
            set
            {
                if (_textValue != value)
                {
                    _textValue = value;
                    SyncDefinition();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextValue)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidationMessage)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDanger)));
                }
            }
        }

        public string NumberText
        {
            get => _numberText;
            set
            {
                if (_numberText != value)
                {
                    _numberText = value;
                    SyncDefinition();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumberText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidationMessage)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDanger)));
                }
            }
        }

        public string SecondaryNumberText
        {
            get => _secondaryNumberText;
            set
            {
                if (_secondaryNumberText == value) return;
                _secondaryNumberText = value;
                SyncDefinition();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryNumberText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidationMessage)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDanger)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void SyncDefinition()
        {
            if (Definition == null) return;
            switch (Definition)
            {
                case DebugInstanceDefinition debug:
                    debug.Message = TextValue ?? "";
                    debug.DelaySeconds = ParseNumber(NumberText);
                    break;
                case StatIncreaseInstanceDefinition stat:
                    stat.StatKey = TextValue ?? "";
                    stat.Amount = ParseNumber(NumberText);
                    break;
                case IncrementProgressInstanceDefinition progress:
                    progress.Amount = ParseNumber(NumberText);
                    break;
                case ModifyTemperatureInstanceDefinition temperature:
                    temperature.TemperatureId = TextValue ?? "";
                    temperature.Amount = ParseNumber(NumberText);
                    break;
                case CutsceneInstanceDefinition cutscene:
                    cutscene.ResourceId = TextValue ?? "";
                    break;
                case DialogInstanceDefinition dialog:
                    dialog.Text = TextValue ?? "";
                    break;
                case DelayInstanceDefinition delay:
                    delay.DurationSeconds = Math.Max(0f, ParseNumber(NumberText));
                    break;
                case ToyActivityInstanceDefinition toy:
                    toy.CapabilityId = TextValue ?? "";
                    toy.Intensity = ParseNumber(NumberText);
                    toy.DurationSeconds = Math.Max(0f, ParseNumber(SecondaryNumberText));
                    break;
                case PromptChoiceInstanceDefinition choice:
                    choice.Prompt = TextValue ?? "";
                    break;
                case PhaseGotoInstanceDefinition phaseGoto:
                    phaseGoto.PhaseExitId = TextValue ?? "";
                    break;
                case SessionGotoInstanceDefinition sessionGoto:
                    sessionGoto.Label = TextValue ?? "";
                    break;
            }
        }

        private static float ParseNumber(string value)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0f;
        }
    }

    /// <summary>One editable PromptChoice option and its recursively-owned sequence.</summary>
    public sealed class PromptChoiceOptionRowData : INotifyPropertyChanged
    {
        private string _label;

        public PromptChoiceOptionRowData(PromptChoiceOptionDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _label = definition.Label ?? "";
        }

        public ActionRowData Parent { get; set; }
        public PromptChoiceOptionDefinition Definition { get; }
        public string OptionId => Definition?.Id;
        public ActionSequenceEditorViewModel ActionSequence { get; set; }

        public string Label
        {
            get => _label;
            set
            {
                value = value ?? "";
                if (_label == value) return;
                _label = value;
                if (Definition != null) Definition.Label = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>One option row of a decision node (label + owned ordered ActionSequence).</summary>
    public sealed class DecisionRowData : INotifyPropertyChanged
    {
        private string _label;

        public string OptionId { get; set; }

        /// <summary>The owning decision node id (authoring resolves options by node).</summary>
        public string NodeId { get; set; }

        /// <summary>"session" or "phase" — which GOTO palette the option's sequence uses.</summary>
        public string Scope { get; set; }

        public string Label
        {
            get => _label;
            set
            {
                if (_label != value)
                {
                    _label = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
                }
            }
        }

        public ObservableCollection<ActionRowData> ActionRows { get; } = new ObservableCollection<ActionRowData>();
        public ActionSequenceEditorViewModel ActionSequence { get; set; }

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
    public sealed class ConnectionViewModel : INotifyPropertyChanged
    {
        private bool _debugActive;

        public ConnectionViewModel(ConnectorViewModel source, ConnectorViewModel target)
        {
            Source = source;
            Target = target;
            Source.IsConnected = true;
            Target.IsConnected = true;
        }

        public ConnectorViewModel Source { get; }
        public ConnectorViewModel Target { get; }

        /// <summary>Live-debug highlight (Ticket 19): the transfer edge into the current node.</summary>
        public bool DebugActive
        {
            get => _debugActive;
            set
            {
                if (_debugActive != value)
                {
                    _debugActive = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DebugActive)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
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

        public List<ActionParameterOption> TemperatureOptions { get; private set; } = new List<ActionParameterOption>();
        public List<ActionParameterOption> StatOptions { get; private set; } = new List<ActionParameterOption>();
        public List<ActionParameterOption> ResourceOptions { get; private set; } = new List<ActionParameterOption>();
        public List<ActionParameterOption> ToyCapabilityOptions { get; private set; } = new List<ActionParameterOption>();

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

        /// <summary>An inline VariableCheck field changed (persist the comparison); the string is the property name.</summary>
        public event Action<GraphNodeViewModel, string> CheckChanged;

        /// <summary>An explicit Action Instance parameter changed.</summary>
        public event Action<GraphNodeViewModel, ActionRowData, string> ActionChanged;

        /// <summary>A decision node's prompt changed (persist it).</summary>
        public event Action<GraphNodeViewModel> DecisionPromptChanged;

        /// <summary>A decision option row's label changed (persist it).</summary>
        public event Action<GraphNodeViewModel, DecisionRowData> DecisionRowChanged;

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

        public void ConfigureActionCatalog(GameContentDefinition content)
        {
            TemperatureOptions = (content?.Temperatures ?? new List<TemperatureDefinition>())
                .Select(item => new ActionParameterOption { Id = item.Id, Name = string.IsNullOrEmpty(item.Title) ? item.Id : item.Title })
                .ToList();
            StatOptions = ConfiguredActionParameters.StatOptions(content);
            ResourceOptions = (content?.Resources ?? new List<ResourceDefinition>())
                .Where(item => string.Equals(item.Kind, "cutscene", StringComparison.OrdinalIgnoreCase))
                .Select(item => new ActionParameterOption { Id = item.Id, Name = string.IsNullOrEmpty(item.Name) ? item.Id : item.Name })
                .ToList();
            ToyCapabilityOptions = (content?.SmartToyCapabilityDefinitions ?? new List<SmartToyCapabilityDefinition>())
                .Select(item => new ActionParameterOption { Id = item.Id, Name = string.IsNullOrEmpty(item.Title) ? item.Id : item.Title })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
                    node.Check.PropertyChanged += (_, args) => CheckChanged?.Invoke(node, args.PropertyName);
                }
                if (node.ActionSequence != null)
                    SubscribeActionSequence(node, node.ActionSequence);
                else
                {
                    foreach (var row in node.ActionRows)
                        row.PropertyChanged += (_, args) => ActionChanged?.Invoke(node, row, args.PropertyName);
                }
                if (node.DecisionScope != null)
                {
                    node.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(GraphNodeViewModel.DecisionPrompt))
                        {
                            DecisionPromptChanged?.Invoke(node);
                        }
                    };
                    foreach (var option in node.DecisionRows)
                    {
                        option.PropertyChanged += (_, args) =>
                        {
                            if (args.PropertyName == nameof(DecisionRowData.Label))
                            {
                                DecisionRowChanged?.Invoke(node, option);
                            }
                        };
                        SubscribeActionSequence(node, option.ActionSequence);
                    }
                }
                Nodes.Add(node);
            }
            foreach (var connection in connections) Connections.Add(connection);
        }

        private void SubscribeActionSequence(GraphNodeViewModel node, ActionSequenceEditorViewModel sequence)
        {
            if (sequence == null) return;
            foreach (var row in sequence.Rows)
            {
                row.PropertyChanged += (_, args) => ActionChanged?.Invoke(node, row, args.PropertyName);
                foreach (var option in row.PromptOptions)
                {
                    option.PropertyChanged += (_, args) => ActionChanged?.Invoke(node, row, args.PropertyName);
                    SubscribeActionSequence(node, option.ActionSequence);
                }
            }
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
            if (source.Owner != null && source.Owner.Inputs.Contains(source))
            {
                // Nodify can start a pending connection from an input socket,
                // but the DB only stores edges as OUTPUT -> NODE (an input id
                // does not exist as a port row and would fail the edge FK).
                // Flip the direction when the drag ended on another node's
                // output; drop the connect otherwise.
                if (target.Owner != null && target.Owner.Outputs.Contains(target)
                    && !ReferenceEquals(source.Owner, target.Owner))
                {
                    var tmp = source;
                    source = target;
                    target = tmp;
                }
                else
                {
                    return;
                }
            }
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

    /// <summary>
    /// Deterministic first-load layout. Stable saved coordinates always win;
    /// only missing node ids are assigned. Connections determine horizontal
    /// layers, while sorted ids determine repeatable vertical slots.
    /// </summary>
    public static class GraphAutoLayout
    {
        public static Dictionary<string, (double X, double Y)> FillMissing(
            IEnumerable<GraphNodeDefinition> nodeDefinitions,
            IEnumerable<GraphEdgeDefinition> edges,
            Dictionary<string, (double X, double Y)> saved)
        {
            var nodes = (nodeDefinitions ?? Enumerable.Empty<GraphNodeDefinition>())
                .Where(node => node != null && !string.IsNullOrEmpty(node.Id))
                .OrderBy(node => node.Id, StringComparer.Ordinal)
                .ToList();
            var result = saved != null
                ? new Dictionary<string, (double X, double Y)>(saved)
                : new Dictionary<string, (double X, double Y)>();
            var ids = new HashSet<string>(nodes.Select(node => node.Id));
            var outputOwner = new Dictionary<string, string>();
            foreach (var node in nodes)
            {
                foreach (var output in node.Outputs ?? new List<GraphOutputDefinition>())
                {
                    if (output != null && !string.IsNullOrEmpty(output.Id)) outputOwner[output.Id] = node.Id;
                }
            }

            var incoming = nodes.ToDictionary(node => node.Id, _ => 0);
            var successors = nodes.ToDictionary(node => node.Id, _ => new List<string>());
            foreach (var edge in (edges ?? Enumerable.Empty<GraphEdgeDefinition>()).OrderBy(edge => edge?.Id, StringComparer.Ordinal))
            {
                if (edge == null || !outputOwner.TryGetValue(edge.SourceOutputId, out var source)
                    || !ids.Contains(edge.TargetNodeId) || !ids.Contains(source)) continue;
                successors[source].Add(edge.TargetNodeId);
                incoming[edge.TargetNodeId]++;
            }

            var layers = nodes.ToDictionary(node => node.Id, _ => 0);
            var queue = new Queue<string>(incoming.Where(pair => pair.Value == 0)
                .Select(pair => pair.Key).OrderBy(id => id, StringComparer.Ordinal));
            var processed = new HashSet<string>();
            while (queue.Count > 0)
            {
                var source = queue.Dequeue();
                if (!processed.Add(source)) continue;
                foreach (var target in successors[source].OrderBy(id => id, StringComparer.Ordinal))
                {
                    layers[target] = Math.Max(layers[target], layers[source] + 1);
                    if (--incoming[target] == 0) queue.Enqueue(target);
                }
            }

            // A previous authoring build could persist several new nodes at the
            // same default coordinate because the live canvas was not refreshed
            // after insertion. Keep the first saved node at an exact coordinate,
            // but treat later duplicates as missing so they are assigned distinct
            // positions and the repaired layout can be persisted by the host.
            var occupied = new List<(double X, double Y)>();
            foreach (var node in nodes)
            {
                if (!result.TryGetValue(node.Id, out var savedPoint)) continue;
                if (occupied.Any(point => Math.Abs(point.X - savedPoint.X) < 0.001 &&
                                          Math.Abs(point.Y - savedPoint.Y) < 0.001))
                {
                    result.Remove(node.Id);
                    continue;
                }
                occupied.Add(savedPoint);
            }

            foreach (var node in nodes)
            {
                if (result.ContainsKey(node.Id)) continue;
                var layer = layers[node.Id];
                var slot = 0;
                var candidate = (X: layer * 260.0, Y: slot * 160.0);
                while (occupied.Any(point => Math.Abs(point.X - candidate.X) < 190.0 && Math.Abs(point.Y - candidate.Y) < 120.0))
                {
                    slot++;
                    candidate = (X: layer * 260.0, Y: slot * 160.0);
                }
                result[node.Id] = candidate;
                occupied.Add(candidate);
            }
            return result;
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

            var effectiveLayout = GraphAutoLayout.FillMissing(session.Graph.Nodes, session.Graph.Edges, layout);
            var nodes = new Dictionary<string, GraphNodeViewModel>();
            var connections = new List<ConnectionViewModel>();

            // Layout: persisted coordinates when present, otherwise a left-to-right
            // grid by graph order, stacked vertically with overlap avoided.
            var column = 0.0;
            var row = 0.0;
            foreach (var node in session.Graph.Nodes)
            {
                var (title, subtitle, kind, refId) = DescribeSessionNode(node);
                var point = effectiveLayout.TryGetValue(node.Id, out var saved)
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
                if (node is SessionDecisionNodeDefinition decision)
                {
                    PhaseGraphViewModel.PopulateDecisionEditing(vm, decision.Prompt, "session", decision.Options,
                        option => option.Id,
                        option => option.Label,
                        option => option.Sequence,
                        option => ActionOwnerScope.SessionDecisionOptionSequence,
                        StatOptions,
                        TemperatureOptions,
                        ResourceOptions,
                        ToyCapabilityOptions,
                        _ => new List<ExitOption>());
                }
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

            var effectiveLayout = GraphAutoLayout.FillMissing(phase.Graph.Nodes, phase.Graph.Edges, layout);
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
                var point = effectiveLayout.TryGetValue(node.Id, out var saved)
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
                if (node is ActionNodeDefinition actionNode)
                {
                    vm.CanEditActions = true;
                    vm.ActionSequence = new ActionSequenceEditorViewModel(vm, actionNode.Sequence?.Id,
                        ActionOwnerScope.PhaseActionSequence,
                        actionNode.Sequence?.Instances,
                        TemperatureOptions, ResourceOptions, ExitOptionsFor(phase), vm.ActionRows,
                        StatOptions, ToyCapabilityOptions);
                }
                if (node is PhaseDecisionNodeDefinition phaseDecision)
                {
                    PopulateDecisionEditing(vm, phaseDecision.Prompt, "phase", phaseDecision.Options,
                        option => option.Id,
                        option => option.Label,
                        option => option.Sequence,
                        option => ActionOwnerScope.ChoiceOptionSequence,
                        StatOptions,
                        TemperatureOptions,
                        ResourceOptions,
                        ToyCapabilityOptions,
                        _ => ExitOptionsFor(phase));
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

        internal static ActionRowData ToActionRow(ActionSequenceEditorViewModel sequence,
            ActionInstanceDefinition instance, int ordinal)
        {
            var owner = sequence.OwnerNode;
            var info = ActionTypeRegistry.ForInstance(instance);
            var row = new ActionRowData
            {
                Owner = owner,
                Sequence = sequence,
                Definition = instance,
                SequenceId = sequence.SequenceId,
                Ordinal = ordinal,
                TypeKey = info.TypeKey,
                DisplayLabel = info.DisplayLabel,
                ParameterOptions = info.TypeKey == ActionTypeKeys.StatIncrease
                    ? sequence.StatOptions
                    : info.TypeKey == ActionTypeKeys.ModifyTemperature ? sequence.TemperatureOptions
                    : info.TypeKey == ActionTypeKeys.Cutscene ? sequence.ResourceOptions : new List<ActionParameterOption>(),
                ExitOptions = sequence.ExitOptions,
            };
            if (info.TypeKey == ActionTypeKeys.ToyActivity)
                row.ParameterOptions = sequence.ToyCapabilityOptions;
            switch (instance)
            {
                case DebugInstanceDefinition debug:
                    row.NumberText = debug.DelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
                    row.TextValue = debug.Message;
                    break;
                case StatIncreaseInstanceDefinition stat:
                    row.NumberText = stat.Amount.ToString("0.###", CultureInfo.InvariantCulture);
                    row.TextValue = stat.StatKey;
                    break;
                case IncrementProgressInstanceDefinition progress:
                    row.NumberText = progress.Amount.ToString("0.###", CultureInfo.InvariantCulture);
                    break;
                case ModifyTemperatureInstanceDefinition temperature:
                    row.NumberText = temperature.Amount.ToString("0.###", CultureInfo.InvariantCulture);
                    row.TextValue = temperature.TemperatureId;
                    break;
                case CutsceneInstanceDefinition cutscene:
                    row.TextValue = cutscene.ResourceId;
                    break;
                case DialogInstanceDefinition dialog:
                    row.TextValue = dialog.Text;
                    break;
                case DelayInstanceDefinition delay:
                    row.NumberText = delay.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                    break;
                case ToyActivityInstanceDefinition toy:
                    var toyIntensity = toy.Intensity.ToString("0.###", CultureInfo.InvariantCulture);
                    var toyDuration = toy.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                    row.SecondaryNumberText = toyDuration;
                    row.NumberText = toyIntensity;
                    row.TextValue = toy.CapabilityId;
                    break;
                case PromptChoiceInstanceDefinition choice:
                    row.TextValue = choice.Prompt;
                    break;
                case PhaseGotoInstanceDefinition phaseGoto:
                    row.TextValue = phaseGoto.PhaseExitId;
                    break;
                case SessionGotoInstanceDefinition sessionGoto:
                    row.TextValue = sessionGoto.Label;
                    break;
            }
            row.PersistedTextValue = row.TextValue;
            row.PersistedNumberText = row.NumberText;
            row.PersistedSecondaryNumberText = row.SecondaryNumberText;
            if (instance is PromptChoiceInstanceDefinition promptChoice)
            {
                foreach (var option in promptChoice.Options)
                {
                    var optionRow = new PromptChoiceOptionRowData
                    (option);
                    optionRow.Parent = row;
                    optionRow.ActionSequence = new ActionSequenceEditorViewModel(owner, option.Sequence?.Id,
                        ActionOwnerScopes.NestedPromptChoice(sequence.OwnerScope),
                        option.Sequence?.Instances,
                        sequence.TemperatureOptions,
                        sequence.ResourceOptions,
                        sequence.ExitOptions,
                        new ObservableCollection<ActionRowData>(),
                        sequence.StatOptions,
                        sequence.ToyCapabilityOptions)
                    {
                        OptionId = option.Id,
                    };
                    row.PromptOptions.Add(optionRow);
                }
            }
            return row;
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

        /// <summary>
        /// Populates the inline decision editor on a node VM from its portable
        /// definition: prompt + 1-3 option rows, each with its owned sequence's
        /// GOTO instances (session-goto or phase-goto by scope).
        /// </summary>
        internal static void PopulateDecisionEditing<TDecisionOption>(
            GraphNodeViewModel vm,
            string prompt,
            string scope,
            List<TDecisionOption> options,
            Func<TDecisionOption, string> optionIdOf,
            Func<TDecisionOption, string> labelOf,
            Func<TDecisionOption, ActionSequenceDefinition> sequenceOf,
            Func<TDecisionOption, ActionOwnerScope> actionScopeOf,
            IEnumerable<ActionParameterOption> statOptions,
            IEnumerable<ActionParameterOption> temperatureOptions,
            IEnumerable<ActionParameterOption> resourceOptions,
            IEnumerable<ActionParameterOption> toyCapabilityOptions,
            Func<string, List<ExitOption>> exitOptionsFor)
        {
            vm.DecisionScope = scope;
            vm.DecisionPrompt = prompt ?? "";
            vm.CanAddOption = options.Count < 3;
            foreach (var option in options)
            {
                var row = new DecisionRowData
                {
                    OptionId = optionIdOf(option),
                    NodeId = vm.Id,
                    Scope = scope,
                    Label = labelOf(option),
                };
                row.ActionSequence = new ActionSequenceEditorViewModel(vm,
                    sequenceOf(option)?.Id,
                    actionScopeOf(option),
                    sequenceOf(option)?.Instances,
                    temperatureOptions,
                    resourceOptions,
                    exitOptionsFor(optionIdOf(option)),
                    row.ActionRows,
                    statOptions,
                    toyCapabilityOptions)
                {
                    OptionId = row.OptionId,
                };
                vm.DecisionRows.Add(row);
            }
        }

        internal static List<ExitOption> ExitOptionsFor(PhaseDefinition phase)
        {
            var options = new List<ExitOption>
            {
                new ExitOption { Id = "", Name = "Unassigned" },
            };
            foreach (var exit in phase?.Exits ?? new List<PhaseExitDefinition>())
            {
                options.Add(new ExitOption { Id = exit.Id, Name = exit.Name });
            }
            return options;
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
            SessionGraph.ConfigureActionCatalog(content);
            PhaseGraph.ConfigureActionCatalog(content);
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
