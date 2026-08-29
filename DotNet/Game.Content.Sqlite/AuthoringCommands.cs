using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 18 semantic undo/redo. An IAuthoringCommand is one focused
    /// authoring edit whose Execute/Undo call explicit transactional
    /// repositories (never raw inverse SQL). Commands capture the state they
    /// need to invert at construction time. Coalescing: commands with a
    /// MergeKey absorb consecutive updates (drag frames, keystrokes) into one
    /// history entry that undoes to the captured original state.
    /// </summary>
    public interface IAuthoringCommand
    {
        /// <summary>Human-readable description for menus/status.</summary>
        string Name { get; }

        /// <summary>Applies the edit (first run and redo share this path).</summary>
        void Execute();

        /// <summary>Inverts the edit.</summary>
        void Undo();

        /// <summary>Non-null when consecutive pushes should coalesce; the key identifies the target.</summary>
        string MergeKey { get; }

        /// <summary>Absorbs an incoming coalesced edit into this command; returns true when handled.</summary>
        bool Merge(IAuthoringCommand incoming);
    }

    /// <summary>
    /// In-memory undo/redo history for the current Workbench run. Every
    /// Execute/Undo/Redo commits its SQLite transaction immediately; a
    /// throwing command surfaces the DB error to the caller (the UI then
    /// reloads from the database, which remains the source of truth).
    /// </summary>
    public sealed class AuthoringCommandStack
    {
        private readonly List<IAuthoringCommand> _undo = new List<IAuthoringCommand>();
        private readonly List<IAuthoringCommand> _redo = new List<IAuthoringCommand>();
        private const int MaxDepth = 200;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;

        /// <summary>Fires after every push/undo/redo (toolbar state refresh).</summary>
        public event Action Changed;

        /// <summary>Executes the command, or coalesces it into the top entry when the merge key matches.</summary>
        public void PushOrMerge(IAuthoringCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.MergeKey != null && _undo.Count > 0 && _undo[_undo.Count - 1].MergeKey == command.MergeKey)
            {
                if (_undo[_undo.Count - 1].Merge(command))
                {
                    _undo[_undo.Count - 1].Execute();
                    // A merged edit is still a new edit: any redo branch is
                    // invalid and toolbar state must refresh immediately.
                    _redo.Clear();
                    Changed?.Invoke();
                    return;
                }
            }
            command.Execute();
            _undo.Add(command);
            if (_undo.Count > MaxDepth) _undo.RemoveAt(0);
            _redo.Clear();
            Changed?.Invoke();
        }

        /// <summary>Undoes the most recent command (throws on DB failure).</summary>
        public void Undo()
        {
            if (_undo.Count == 0) return;
            var command = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            command.Undo();
            _redo.Add(command);
            Changed?.Invoke();
        }

        /// <summary>Re-applies the most recently undone command (throws on DB failure).</summary>
        public void Redo()
        {
            if (_redo.Count == 0) return;
            var command = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            command.Execute();
            _undo.Add(command);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
        }
    }

    /// <summary>Shared plumbing: one opened connection per Execute/Undo, Name, merge defaults.</summary>
    public abstract class AuthoringCommandBase : IAuthoringCommand
    {
        private readonly Func<DbConnection> _conn;

        protected AuthoringCommandBase(Func<DbConnection> conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        public abstract string Name { get; }

        protected abstract void ExecuteCore(DbConnection connection);
        protected abstract void UndoCore(DbConnection connection);

        public virtual string MergeKey => null;
        public virtual bool Merge(IAuthoringCommand incoming) => false;

        public void Execute()
        {
            using (var connection = _conn()) ExecuteCore(connection);
        }

        public void Undo()
        {
            using (var connection = _conn()) UndoCore(connection);
        }
    }

    // ==================================================================
    // Graph topology
    // ==================================================================

    /// <summary>Node drag: coalesces per drag into one command; undo snaps back to the original spot.</summary>
    public sealed class MoveNodeCommand : AuthoringCommandBase
    {
        private readonly string _scope;      // "session" | "phase"
        private readonly string _parentId;
        private readonly string _nodeId;
        private readonly Point2 _original;
        private Point2 _current;

        public MoveNodeCommand(Func<DbConnection> conn, string scope, string parentId, string nodeId,
            Point2 original, Point2 current) : base(conn)
        {
            _scope = scope; _parentId = parentId; _nodeId = nodeId;
            _original = original; _current = current;
        }

        public override string Name => "Move node";
        public override string MergeKey => "move:" + _scope + ":" + _nodeId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is MoveNodeCommand move) { _current = move._current; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection)
        {
            if (_scope == "session")
                AuthoringLayoutRepository.SaveSessionNodePosition(connection, _parentId, _nodeId, _current.X, _current.Y);
            else
                AuthoringLayoutRepository.SavePhaseNodePosition(connection, _parentId, _nodeId, _current.X, _current.Y);
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_scope == "session")
                AuthoringLayoutRepository.SaveSessionNodePosition(connection, _parentId, _nodeId, _original.X, _original.Y);
            else
                AuthoringLayoutRepository.SavePhaseNodePosition(connection, _parentId, _nodeId, _original.X, _original.Y);
        }
    }

    /// <summary>Simple (x, y) value type so the command layer stays WPF-free.</summary>
    public struct Point2
    {
        public double X;
        public double Y;
        public Point2(double x, double y) { X = x; Y = y; }
    }

    /// <summary>Adds one session graph node + its layout row.</summary>
    public sealed class AddSessionNodeCommand : AuthoringCommandBase
    {
        private readonly string _sessionId;
        private readonly SessionGraphNodeDefinition _node;
        private readonly Point2 _location;

        public AddSessionNodeCommand(Func<DbConnection> conn, string sessionId, SessionGraphNodeDefinition node, Point2 location)
            : base(conn) { _sessionId = sessionId; _node = node; _location = location; }

        public override string Name => "Add node";

        protected override void ExecuteCore(DbConnection connection)
        {
            SessionGraphRepository.AddNode(connection, _sessionId, _node);
            AuthoringLayoutRepository.SaveSessionNodePosition(connection, _sessionId, _node.Id, _location.X, _location.Y);
        }

        protected override void UndoCore(DbConnection connection)
        {
            SessionGraphRepository.RemoveNode(connection, _sessionId, _node.Id);
            AuthoringLayoutRepository.DeleteSessionNodePosition(connection, _sessionId, _node.Id);
        }
    }

    /// <summary>Adds one phase graph node + its layout row.</summary>
    public sealed class AddPhaseNodeCommand : AuthoringCommandBase
    {
        private readonly string _phaseId;
        private readonly PhaseGraphNodeDefinition _node;
        private readonly Point2 _location;

        public AddPhaseNodeCommand(Func<DbConnection> conn, string phaseId, PhaseGraphNodeDefinition node, Point2 location)
            : base(conn) { _phaseId = phaseId; _node = node; _location = location; }

        public override string Name => "Add node";

        protected override void ExecuteCore(DbConnection connection)
        {
            PhaseGraphRepository.AddNode(connection, _phaseId, _node);
            AuthoringLayoutRepository.SavePhaseNodePosition(connection, _phaseId, _node.Id, _location.X, _location.Y);
        }

        protected override void UndoCore(DbConnection connection)
        {
            PhaseGraphRepository.RemoveNode(connection, _phaseId, _node.Id);
            AuthoringLayoutRepository.DeletePhaseNodePosition(connection, _phaseId, _node.Id);
        }
    }

    /// <summary>
    /// Deletes a session node; undo restores the node, its edges and its
    /// layout position as one semantic edit (edge ids are regenerated).
    /// </summary>
    public sealed class DeleteSessionNodeCommand : AuthoringCommandBase
    {
        private readonly string _sessionId;
        private readonly SessionGraphNodeDefinition _node;
        private readonly List<GraphEdgeDefinition> _edges;
        private readonly Point2? _position;

        public DeleteSessionNodeCommand(Func<DbConnection> conn, string sessionId, SessionGraphNodeDefinition node,
            List<GraphEdgeDefinition> edges, Point2? position) : base(conn)
        {
            _sessionId = sessionId; _node = node; _edges = edges; _position = position;
        }

        public override string Name => "Delete node";

        protected override void ExecuteCore(DbConnection connection)
        {
            SessionGraphRepository.RemoveNode(connection, _sessionId, _node.Id);
            if (_position.HasValue)
                AuthoringLayoutRepository.DeleteSessionNodePosition(connection, _sessionId, _node.Id);
        }

        protected override void UndoCore(DbConnection connection)
        {
            SessionGraphRepository.AddNode(connection, _sessionId, _node);
            foreach (var edge in _edges)
            {
                SessionGraphRepository.AddEdge(connection, _sessionId, new GraphEdgeDefinition
                {
                    Id = "se-" + Guid.NewGuid().ToString("N"),
                    SourceOutputId = edge.SourceOutputId,
                    TargetNodeId = edge.TargetNodeId,
                });
            }
            if (_position.HasValue)
                AuthoringLayoutRepository.SaveSessionNodePosition(connection, _sessionId, _node.Id, _position.Value.X, _position.Value.Y);
        }
    }

    /// <summary>Deletes a phase node; undo restores node + edges + layout.</summary>
    public sealed class DeletePhaseNodeCommand : AuthoringCommandBase
    {
        private readonly string _phaseId;
        private readonly PhaseGraphNodeDefinition _node;
        private readonly List<GraphEdgeDefinition> _edges;
        private readonly Point2? _position;

        public DeletePhaseNodeCommand(Func<DbConnection> conn, string phaseId, PhaseGraphNodeDefinition node,
            List<GraphEdgeDefinition> edges, Point2? position) : base(conn)
        {
            _phaseId = phaseId; _node = node; _edges = edges; _position = position;
        }

        public override string Name => "Delete node";

        protected override void ExecuteCore(DbConnection connection)
        {
            PhaseGraphRepository.RemoveNode(connection, _phaseId, _node.Id);
            if (_position.HasValue)
                AuthoringLayoutRepository.DeletePhaseNodePosition(connection, _phaseId, _node.Id);
        }

        protected override void UndoCore(DbConnection connection)
        {
            PhaseGraphRepository.AddNode(connection, _phaseId, _node);
            foreach (var edge in _edges)
            {
                PhaseGraphRepository.AddEdge(connection, _phaseId, new GraphEdgeDefinition
                {
                    Id = "pe-" + Guid.NewGuid().ToString("N"),
                    SourceOutputId = edge.SourceOutputId,
                    TargetNodeId = edge.TargetNodeId,
                });
            }
            if (_position.HasValue)
                AuthoringLayoutRepository.SavePhaseNodePosition(connection, _phaseId, _node.Id, _position.Value.X, _position.Value.Y);
        }
    }

    /// <summary>Connects one output socket to a target node; undo removes the edge and restores any replaced edge.</summary>
    public sealed class ConnectSessionCommand : AuthoringCommandBase
    {
        private readonly string _sessionId;
        private readonly string _sourceOutputId;
        private readonly string _targetNodeId;
        private readonly string _replacedTarget;

        public ConnectSessionCommand(Func<DbConnection> conn, string sessionId, string sourceOutputId, string targetNodeId, string replacedTarget)
            : base(conn)
        {
            _sessionId = sessionId; _sourceOutputId = sourceOutputId; _targetNodeId = targetNodeId; _replacedTarget = replacedTarget;
        }

        public override string Name => "Connect";

        protected override void ExecuteCore(DbConnection connection)
        {
            SessionGraphRepository.RemoveEdgesFromSource(connection, _sourceOutputId);
            SessionGraphRepository.AddEdge(connection, _sessionId, new GraphEdgeDefinition
            {
                Id = "se-" + Guid.NewGuid().ToString("N"),
                SourceOutputId = _sourceOutputId,
                TargetNodeId = _targetNodeId,
            });
        }

        protected override void UndoCore(DbConnection connection)
        {
            SessionGraphRepository.RemoveEdgesFromSource(connection, _sourceOutputId);
            if (!string.IsNullOrEmpty(_replacedTarget))
            {
                SessionGraphRepository.AddEdge(connection, _sessionId, new GraphEdgeDefinition
                {
                    Id = "se-" + Guid.NewGuid().ToString("N"),
                    SourceOutputId = _sourceOutputId,
                    TargetNodeId = _replacedTarget,
                });
            }
        }
    }

    public sealed class ConnectPhaseCommand : AuthoringCommandBase
    {
        private readonly string _phaseId;
        private readonly string _sourceOutputId;
        private readonly string _targetNodeId;
        private readonly string _replacedTarget;

        public ConnectPhaseCommand(Func<DbConnection> conn, string phaseId, string sourceOutputId, string targetNodeId, string replacedTarget)
            : base(conn)
        {
            _phaseId = phaseId; _sourceOutputId = sourceOutputId; _targetNodeId = targetNodeId; _replacedTarget = replacedTarget;
        }

        public override string Name => "Connect";

        protected override void ExecuteCore(DbConnection connection)
        {
            PhaseGraphRepository.RemoveEdgesFromSource(connection, _sourceOutputId);
            PhaseGraphRepository.AddEdge(connection, _phaseId, new GraphEdgeDefinition
            {
                Id = "pe-" + Guid.NewGuid().ToString("N"),
                SourceOutputId = _sourceOutputId,
                TargetNodeId = _targetNodeId,
            });
        }

        protected override void UndoCore(DbConnection connection)
        {
            PhaseGraphRepository.RemoveEdgesFromSource(connection, _sourceOutputId);
            if (!string.IsNullOrEmpty(_replacedTarget))
            {
                PhaseGraphRepository.AddEdge(connection, _phaseId, new GraphEdgeDefinition
                {
                    Id = "pe-" + Guid.NewGuid().ToString("N"),
                    SourceOutputId = _sourceOutputId,
                    TargetNodeId = _replacedTarget,
                });
            }
        }
    }

    /// <summary>Disconnect: removes edges leaving a socket; undo re-wires the captured target.</summary>
    public sealed class DisconnectSessionCommand : AuthoringCommandBase
    {
        private readonly string _sessionId;
        private readonly string _sourceOutputId;
        private readonly string _targetNodeId;

        public DisconnectSessionCommand(Func<DbConnection> conn, string sessionId, string sourceOutputId, string targetNodeId)
            : base(conn) { _sessionId = sessionId; _sourceOutputId = sourceOutputId; _targetNodeId = targetNodeId; }

        public override string Name => "Disconnect";

        protected override void ExecuteCore(DbConnection connection)
        {
            SessionGraphRepository.RemoveEdgesFromSource(connection, _sourceOutputId);
        }

        protected override void UndoCore(DbConnection connection)
        {
            SessionGraphRepository.AddEdge(connection, _sessionId, new GraphEdgeDefinition
            {
                Id = "se-" + Guid.NewGuid().ToString("N"),
                SourceOutputId = _sourceOutputId,
                TargetNodeId = _targetNodeId,
            });
        }
    }

    public sealed class DisconnectPhaseCommand : AuthoringCommandBase
    {
        private readonly string _phaseId;
        private readonly string _sourceOutputId;
        private readonly string _targetNodeId;

        public DisconnectPhaseCommand(Func<DbConnection> conn, string phaseId, string sourceOutputId, string targetNodeId)
            : base(conn) { _phaseId = phaseId; _sourceOutputId = sourceOutputId; _targetNodeId = targetNodeId; }

        public override string Name => "Disconnect";

        protected override void ExecuteCore(DbConnection connection)
        {
            PhaseGraphRepository.RemoveEdgesFromSource(connection, _sourceOutputId);
        }

        protected override void UndoCore(DbConnection connection)
        {
            PhaseGraphRepository.AddEdge(connection, _phaseId, new GraphEdgeDefinition
            {
                Id = "pe-" + Guid.NewGuid().ToString("N"),
                SourceOutputId = _sourceOutputId,
                TargetNodeId = _targetNodeId,
            });
        }
    }

    // ==================================================================
    // Session/Phase metadata
    // ==================================================================

    /// <summary>Creates a session with its singular Start node and layout row.</summary>
    public sealed class CreateSessionCommand : AuthoringCommandBase
    {
        private readonly string _id;
        private readonly string _title;
        private readonly string _typeId;

        public CreateSessionCommand(Func<DbConnection> conn, string id, string title, string typeId) : base(conn)
        {
            _id = id; _title = title; _typeId = typeId;
        }

        public override string Name => "Create session";
        protected override void ExecuteCore(DbConnection connection) => SessionRepository.CreateWithStart(connection, _id, _title, _typeId);
        protected override void UndoCore(DbConnection connection) => SessionRepository.Delete(connection, _id);
    }

    /// <summary>Creates a phase with its singular Entry node and layout row.</summary>
    public sealed class CreatePhaseCommand : AuthoringCommandBase
    {
        private readonly string _id;
        private readonly string _title;

        public CreatePhaseCommand(Func<DbConnection> conn, string id, string title) : base(conn)
        {
            _id = id; _title = title;
        }

        public override string Name => "Create phase";
        protected override void ExecuteCore(DbConnection connection) => PhaseRepository.CreateWithEntry(connection, _id, _title);
        protected override void UndoCore(DbConnection connection) => PhaseRepository.Delete(connection, _id);
    }

    /// <summary>Deletes a session and restores its full graph/layout snapshot on undo.</summary>
    public sealed class DeleteSessionCommand : AuthoringCommandBase
    {
        private readonly SessionDefinition _session;
        private readonly List<(string NodeId, double X, double Y)> _layout;

        public DeleteSessionCommand(Func<DbConnection> conn, SessionDefinition session,
            List<(string NodeId, double X, double Y)> layout) : base(conn)
        {
            _session = session; _layout = layout;
        }

        public override string Name => "Delete session";
        protected override void ExecuteCore(DbConnection connection) => SessionRepository.Delete(connection, _session.Id);
        protected override void UndoCore(DbConnection connection)
        {
            ReuseWriter.WriteClonedSession(connection, _session);
            foreach (var pair in _layout)
                AuthoringLayoutRepository.SaveSessionNodePosition(connection, _session.Id, pair.NodeId, pair.X, pair.Y);
        }
    }

    /// <summary>Deletes a phase and restores its full graph/layout snapshot on undo.</summary>
    public sealed class DeletePhaseCommand : AuthoringCommandBase
    {
        private readonly PhaseDefinition _phase;
        private readonly List<(string NodeId, double X, double Y)> _layout;

        public DeletePhaseCommand(Func<DbConnection> conn, PhaseDefinition phase,
            List<(string NodeId, double X, double Y)> layout) : base(conn)
        {
            _phase = phase; _layout = layout;
        }

        public override string Name => "Delete phase";
        protected override void ExecuteCore(DbConnection connection) => PhaseRepository.Delete(connection, _phase.Id);
        protected override void UndoCore(DbConnection connection)
        {
            ReuseWriter.WriteClonedPhase(connection, _phase);
            foreach (var pair in _layout)
                AuthoringLayoutRepository.SavePhaseNodePosition(connection, _phase.Id, pair.NodeId, pair.X, pair.Y);
        }
    }

    /// <summary>Session title rename (coalesced per keystroke burst).</summary>
    public sealed class RenameSessionCommand : AuthoringCommandBase
    {
        private readonly string _sessionId;
        private readonly string _oldTitle;
        private string _newTitle;

        public RenameSessionCommand(Func<DbConnection> conn, string sessionId, string oldTitle, string newTitle)
            : base(conn) { _sessionId = sessionId; _oldTitle = oldTitle; _newTitle = newTitle; }

        public override string Name => "Rename session";
        public override string MergeKey => "sessiontitle:" + _sessionId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is RenameSessionCommand rename) { _newTitle = rename._newTitle; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection) => SessionRepository.Rename(connection, _sessionId, _newTitle);
        protected override void UndoCore(DbConnection connection) => SessionRepository.Rename(connection, _sessionId, _oldTitle);
    }

    /// <summary>Phase title rename (coalesced per keystroke burst).</summary>
    public sealed class RenamePhaseCommand : AuthoringCommandBase
    {
        private readonly string _phaseId;
        private readonly string _oldTitle;
        private string _newTitle;

        public RenamePhaseCommand(Func<DbConnection> conn, string phaseId, string oldTitle, string newTitle)
            : base(conn) { _phaseId = phaseId; _oldTitle = oldTitle; _newTitle = newTitle; }

        public override string Name => "Rename phase";
        public override string MergeKey => "phasetitle:" + _phaseId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is RenamePhaseCommand rename) { _newTitle = rename._newTitle; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection) => PhaseRepository.Rename(connection, _phaseId, _newTitle);
        protected override void UndoCore(DbConnection connection) => PhaseRepository.Rename(connection, _phaseId, _oldTitle);
    }

    /// <summary>Replaces the phase Card tag query (ALL/ANY, include-only) as one coalesced metadata edit.</summary>
    public sealed class SetPhaseCardQueryCommand : AuthoringCommandBase
    {
        private readonly string _phaseId;
        private readonly List<string> _oldAll;
        private readonly List<string> _oldAny;
        private List<string> _newAll;
        private List<string> _newAny;

        public SetPhaseCardQueryCommand(Func<DbConnection> conn, string phaseId,
            IReadOnlyList<string> oldAll, IReadOnlyList<string> oldAny,
            IReadOnlyList<string> newAll, IReadOnlyList<string> newAny) : base(conn)
        {
            _phaseId = phaseId;
            _oldAll = new List<string>(oldAll ?? new List<string>());
            _oldAny = new List<string>(oldAny ?? new List<string>());
            _newAll = new List<string>(newAll ?? new List<string>());
            _newAny = new List<string>(newAny ?? new List<string>());
        }

        public override string Name => "Edit phase card query";
        public override string MergeKey => "phasecardquery:" + _phaseId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (!(incoming is SetPhaseCardQueryCommand query)) return false;
            _newAll = new List<string>(query._newAll);
            _newAny = new List<string>(query._newAny);
            return true;
        }

        protected override void ExecuteCore(DbConnection connection) =>
            PhaseRepository.ReplaceCardQuery(connection, _phaseId, _newAll, _newAny);

        protected override void UndoCore(DbConnection connection) =>
            PhaseRepository.ReplaceCardQuery(connection, _phaseId, _oldAll, _oldAny);
    }

    /// <summary>Session type change (discrete ComboBox pick).</summary>
    public sealed class SetSessionTypeCommand : AuthoringCommandBase
    {
        private readonly string _sessionId;
        private readonly string _oldTypeId;
        private readonly string _newTypeId;

        public SetSessionTypeCommand(Func<DbConnection> conn, string sessionId, string oldTypeId, string newTypeId)
            : base(conn) { _sessionId = sessionId; _oldTypeId = oldTypeId; _newTypeId = newTypeId; }

        public override string Name => "Change session type";
        protected override void ExecuteCore(DbConnection connection) => SessionRepository.SetSessionType(connection, _sessionId, _newTypeId);
        protected override void UndoCore(DbConnection connection) => SessionRepository.SetSessionType(connection, _sessionId, _oldTypeId);
    }

    /// <summary>Decision prompt edit (coalesced; both session and phase scope).</summary>
    public sealed class SetDecisionPromptCommand : AuthoringCommandBase
    {
        private readonly string _scope;
        private readonly string _nodeId;
        private readonly string _oldPrompt;
        private string _newPrompt;

        public SetDecisionPromptCommand(Func<DbConnection> conn, string scope, string nodeId, string oldPrompt, string newPrompt)
            : base(conn) { _scope = scope; _nodeId = nodeId; _oldPrompt = oldPrompt; _newPrompt = newPrompt; }

        public override string Name => "Edit prompt";
        public override string MergeKey => "prompt:" + _scope + ":" + _nodeId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is SetDecisionPromptCommand prompt) { _newPrompt = prompt._newPrompt; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection)
        {
            if (_scope == "session") SessionDecisionRepository.UpdatePrompt(connection, _nodeId, _newPrompt);
            else PhaseDecisionRepository.UpdatePrompt(connection, _nodeId, _newPrompt);
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_scope == "session") SessionDecisionRepository.UpdatePrompt(connection, _nodeId, _oldPrompt);
            else PhaseDecisionRepository.UpdatePrompt(connection, _nodeId, _oldPrompt);
        }
    }

    /// <summary>
    /// Inline VariableCheck edit (one field per command, coalesced per field).
    /// Holds the full original comparison; each merge only swaps the edited field.
    /// </summary>
    public sealed class UpdateCheckCommand : AuthoringCommandBase
    {
        private readonly string _nodeId;
        private readonly string _field;
        private readonly string _oldSource;
        private readonly string _oldKey;
        private readonly string _oldOp;
        private readonly float _oldValue;
        private string _newSource;
        private string _newKey;
        private string _newOp;
        private float _newValue;

        public UpdateCheckCommand(Func<DbConnection> conn, string nodeId, string field,
            string oldSource, string oldKey, string oldOp, float oldValue,
            string newSource, string newKey, string newOp, float newValue) : base(conn)
        {
            _nodeId = nodeId; _field = field;
            _oldSource = oldSource; _oldKey = oldKey; _oldOp = oldOp; _oldValue = oldValue;
            _newSource = newSource; _newKey = newKey; _newOp = newOp; _newValue = newValue;
        }

        public override string Name => "Edit check";
        public override string MergeKey => "check:" + _nodeId + ":" + _field;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is UpdateCheckCommand check)
            {
                _newSource = check._newSource; _newKey = check._newKey;
                _newOp = check._newOp; _newValue = check._newValue;
                return true;
            }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection)
        {
            PhaseGraphRepository.UpdateVariableCheck(connection, _nodeId,
                ParseSourceKind(_newSource), _newKey, ParseOperator(_newOp), _newValue);
        }

        protected override void UndoCore(DbConnection connection)
        {
            PhaseGraphRepository.UpdateVariableCheck(connection, _nodeId,
                ParseSourceKind(_oldSource), _oldKey, ParseOperator(_oldOp), _oldValue);
        }

        private static VariableSourceKind ParseSourceKind(string source)
        {
            if (source == "temperature") return VariableSourceKind.Temperature;
            if (source == "stat") return VariableSourceKind.Stat;
            return VariableSourceKind.PhaseProgress;
        }

        private static VariableCompareOperator ParseOperator(string op)
        {
            if (op == "<") return VariableCompareOperator.LessThan;
            if (op == "<=") return VariableCompareOperator.LessThanOrEqual;
            if (op == "==") return VariableCompareOperator.Equal;
            if (op == "!=") return VariableCompareOperator.NotEqual;
            if (op == ">") return VariableCompareOperator.GreaterThan;
            return VariableCompareOperator.GreaterThanOrEqual;
        }
    }

    // ==================================================================
    // Phase exits
    // ==================================================================

    /// <summary>Phase exit rename (coalesced per keystroke burst).</summary>
    public sealed class RenameExitCommand : AuthoringCommandBase
    {
        private readonly string _exitId;
        private readonly string _oldName;
        private string _newName;

        public RenameExitCommand(Func<DbConnection> conn, string exitId, string oldName, string newName)
            : base(conn) { _exitId = exitId; _oldName = oldName; _newName = newName; }

        public override string Name => "Rename exit";
        public override string MergeKey => "exitname:" + _exitId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is RenameExitCommand rename) { _newName = rename._newName; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection) => PhaseExitRepository.Rename(connection, _exitId, _newName);
        protected override void UndoCore(DbConnection connection) => PhaseExitRepository.Rename(connection, _exitId, _oldName);
    }

    /// <summary>
    /// Creates an exit + its projected sockets on every placement; undo deletes
    /// the exit (cascade). Ordinal &lt; 0 (the shared-port flow) is computed from
    /// the current exit count at Execute time, because the clone phase only
    /// exists mid-execution.
    /// </summary>
    public sealed class CreateExitCommand : AuthoringCommandBase
    {
        private readonly string _phaseId;
        private readonly PhaseExitDefinition _exit;
        private readonly int _ordinal;

        public CreateExitCommand(Func<DbConnection> conn, string phaseId, PhaseExitDefinition exit, int ordinal)
            : base(conn) { _phaseId = phaseId; _exit = exit; _ordinal = ordinal; }

        public override string Name => "Add exit";

        protected override void ExecuteCore(DbConnection connection)
        {
            var ordinal = _ordinal >= 0 ? _ordinal : CurrentExitCount(connection, _phaseId);
            PhaseExitRepository.Create(connection, _phaseId, _exit, ordinal);
            PhaseExitRepository.SyncProjectedSockets(connection, _phaseId, _exit.Id, ordinal);
        }

        protected override void UndoCore(DbConnection connection)
        {
            PhaseExitRepository.Delete(connection, _exit.Id);
        }

        private static int CurrentExitCount(DbConnection connection, string phaseId)
        {
            var count = 0;
            Sql.QueryAll(connection,
                "SELECT COUNT(*) FROM phase_exit WHERE phase_id = @phase;",
                reader => count = reader.GetInt32(0), ("phase", phaseId));
            return count;
        }
    }

    /// <summary>
    /// Deletes an exit on a clone phase that only exists after Make Unique ran
    /// (shared-port flow). The cloned exit id is passed explicitly and its
    /// snapshot (id, ordinal, projected edges) captured then, so Undo restores
    /// the exit, its projected sockets and their edges on the clone.
    /// </summary>
    public sealed class DeleteExitOnCloneCommand : AuthoringCommandBase
    {
        private readonly string _newPhaseId;
        private readonly string _exitId;
        private PhaseExitDefinition _exit;
        private int _ordinal = -1;
        private List<ProjectedEdgeSnapshot> _projectedEdges = new List<ProjectedEdgeSnapshot>();
        private List<PhaseGotoSnapshot> _gotoSnapshots = new List<PhaseGotoSnapshot>();

        public DeleteExitOnCloneCommand(Func<DbConnection> conn, string newPhaseId, string exitId) : base(conn)
        {
            _newPhaseId = newPhaseId; _exitId = exitId;
        }

        public override string Name => "Delete exit";

        protected override void ExecuteCore(DbConnection connection)
        {
            var exits = PhaseExitRepository.List(connection, _newPhaseId);
            var match = exits.Find(x => x.Id == _exitId);
            if (match == null) throw new InvalidOperationException($"Exit '{_exitId}' not found on the unique clone.");
            _exit = match;
            _ordinal = exits.IndexOf(match);
            _projectedEdges = AuthoringUndo.ExitProjectedEdges(connection, match.Id);
            _gotoSnapshots = AuthoringUndo.SnapshotPhaseGotosByExit(connection, match.Id);
            PhaseExitRepository.ForceDelete(connection, match.Id);
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_exit == null) return;
            PhaseExitRepository.Create(connection, _newPhaseId, _exit, _ordinal);
            PhaseExitRepository.SyncProjectedSockets(connection, _newPhaseId, _exit.Id, _ordinal);
            foreach (var edge in _projectedEdges)
            {
                Sql.Execute(connection, null,
                    "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) " +
                    "VALUES (@id, @session, @port, @target);",
                    ("id", edge.EdgeId), ("session", edge.SessionId),
                    ("port", edge.PortId), ("target", edge.TargetNodeId));
            }
            foreach (var gotoSnapshot in _gotoSnapshots)
                PhaseGraphRepository.SetPhaseGotoExit(connection, gotoSnapshot.InstanceId, gotoSnapshot.ExitId);
        }
    }

    /// <summary>
    /// Deletes an exit; undo restores the exit, its projected sockets and any
    /// edges wired from them as one semantic edit. Used GOTO assignments are
    /// cleared on execute and restored with the original exit id on undo.
    /// </summary>
    public sealed class DeleteExitCommand : AuthoringCommandBase
    {
        private readonly string _phaseId;
        private readonly PhaseExitDefinition _exit;
        private readonly int _ordinal;
        private readonly List<ProjectedEdgeSnapshot> _projectedEdges;
        private List<PhaseGotoSnapshot> _gotoSnapshots = new List<PhaseGotoSnapshot>();

        public DeleteExitCommand(Func<DbConnection> conn, string phaseId, PhaseExitDefinition exit, int ordinal,
            List<ProjectedEdgeSnapshot> projectedEdges) : base(conn)
        {
            _phaseId = phaseId; _exit = exit; _ordinal = ordinal; _projectedEdges = projectedEdges;
        }

        public override string Name => "Delete exit";

        protected override void ExecuteCore(DbConnection connection)
        {
            _gotoSnapshots = AuthoringUndo.SnapshotPhaseGotosByExit(connection, _exit.Id);
            PhaseExitRepository.ForceDelete(connection, _exit.Id);
        }

        protected override void UndoCore(DbConnection connection)
        {
            PhaseExitRepository.Create(connection, _phaseId, _exit, _ordinal);
            PhaseExitRepository.SyncProjectedSockets(connection, _phaseId, _exit.Id, _ordinal);
            foreach (var edge in _projectedEdges)
            {
                Sql.Execute(connection, null,
                    "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) " +
                    "VALUES (@id, @session, @port, @target);",
                    ("id", edge.EdgeId), ("session", edge.SessionId),
                    ("port", edge.PortId), ("target", edge.TargetNodeId));
            }
            foreach (var gotoSnapshot in _gotoSnapshots)
                PhaseGraphRepository.SetPhaseGotoExit(connection, gotoSnapshot.InstanceId, gotoSnapshot.ExitId);
        }
    }

    // ==================================================================
    // Action instances (PhaseGoto / SessionGoto)
    // ==================================================================

    /// <summary>Adds one registry-defined Action Instance to an owned sequence.</summary>
    public sealed class AddActionInstanceCommand : AuthoringCommandBase
    {
        private readonly string _sequenceId;
        private readonly ActionOwnerScope _scope;
        private readonly string _typeKey;
        private readonly string _instanceId;
        private readonly ActionInstanceDefinition _configuredInstance;
        private readonly int _ordinal;

        public AddActionInstanceCommand(Func<DbConnection> conn, string sequenceId, ActionOwnerScope scope,
            string typeKey, string instanceId) : this(conn, sequenceId, scope, typeKey, instanceId, null, -1)
        {
        }

        public AddActionInstanceCommand(Func<DbConnection> conn, string sequenceId, ActionOwnerScope scope,
            string typeKey, string instanceId, ActionInstanceDefinition configuredInstance, int ordinal = -1) : base(conn)
        {
            _sequenceId = sequenceId; _scope = scope; _typeKey = typeKey; _instanceId = instanceId;
            _configuredInstance = configuredInstance;
            _ordinal = ordinal;
        }

        public override string Name => "Add action";
        protected override void ExecuteCore(DbConnection connection)
        {
            if (_configuredInstance == null)
            {
                ActionInstanceRepository.AppendDefault(connection, _sequenceId, _scope, _typeKey, _instanceId);
                return;
            }
            if (_ordinal >= 0)
                ActionInstanceRepository.Insert(connection, _sequenceId, _scope, _configuredInstance, _ordinal);
            else
                ActionInstanceRepository.Append(connection, _sequenceId, _scope, _configuredInstance);
        }
        protected override void UndoCore(DbConnection connection) => ActionInstanceRepository.Delete(connection, _instanceId);
    }

    /// <summary>Deletes one explicit Action Instance and restores its exact ordinal on undo.</summary>
    public sealed class RemoveActionInstanceCommand : AuthoringCommandBase
    {
        private readonly string _sequenceId;
        private readonly ActionInstanceDefinition _instance;
        private readonly int _ordinal;

        public RemoveActionInstanceCommand(Func<DbConnection> conn, string sequenceId,
            ActionInstanceDefinition instance, int ordinal) : base(conn)
        {
            _sequenceId = sequenceId; _instance = instance; _ordinal = ordinal;
        }

        public override string Name => "Remove action";
        protected override void ExecuteCore(DbConnection connection) => ActionInstanceRepository.Delete(connection, _instance.Id);
        protected override void UndoCore(DbConnection connection)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ActionSequenceWriter.WriteSingleAt(connection, transaction, _sequenceId, _instance, _ordinal);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }
    }

    /// <summary>Updates the explicit parameter fields of one Action Instance.</summary>
    public sealed class UpdateActionInstanceCommand : AuthoringCommandBase
    {
        private readonly string _instanceId;
        private readonly string _typeKey;
        private readonly string _oldText;
        private readonly float _oldNumber;
        private string _newText;
        private float _newNumber;

        public UpdateActionInstanceCommand(Func<DbConnection> conn, string instanceId, string typeKey,
            string oldText, float oldNumber, string newText, float newNumber) : base(conn)
        {
            _instanceId = instanceId; _typeKey = typeKey; _oldText = oldText; _oldNumber = oldNumber;
            _newText = newText; _newNumber = newNumber;
        }

        public override string Name => "Edit action";
        public override string MergeKey => "action:" + _instanceId + ":" + _typeKey;
        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is UpdateActionInstanceCommand update && update._instanceId == _instanceId && update._typeKey == _typeKey)
            {
                _newText = update._newText; _newNumber = update._newNumber; return true;
            }
            return false;
        }
        protected override void ExecuteCore(DbConnection connection) => ActionInstanceRepository.Update(connection, _instanceId, _typeKey, _newText, _newNumber);
        protected override void UndoCore(DbConnection connection) => ActionInstanceRepository.Update(connection, _instanceId, _typeKey, _oldText, _oldNumber);
    }

    /// <summary>Swaps adjacent Action Instance ordinals.</summary>
    public sealed class MoveActionInstanceCommand : AuthoringCommandBase
    {
        private readonly string _sequenceId;
        private readonly string _instanceId;
        private readonly string _otherInstanceId;

        public MoveActionInstanceCommand(Func<DbConnection> conn, string sequenceId, string instanceId, string otherInstanceId)
            : base(conn) { _sequenceId = sequenceId; _instanceId = instanceId; _otherInstanceId = otherInstanceId; }

        public override string Name => "Reorder action";
        protected override void ExecuteCore(DbConnection connection) => ActionInstanceRepository.Move(connection, _sequenceId, _instanceId, _otherInstanceId);
        protected override void UndoCore(DbConnection connection) => ActionInstanceRepository.Move(connection, _sequenceId, _otherInstanceId, _instanceId);
    }

    /// <summary>Moves an action directly to an ordinal; one drag is one undo step.</summary>
    public sealed class MoveActionInstanceToOrdinalCommand : AuthoringCommandBase
    {
        private readonly string _sequenceId;
        private readonly string _instanceId;
        private readonly int _oldOrdinal;
        private readonly int _newOrdinal;

        public MoveActionInstanceToOrdinalCommand(Func<DbConnection> conn, string sequenceId,
            string instanceId, int oldOrdinal, int newOrdinal) : base(conn)
        {
            _sequenceId = sequenceId; _instanceId = instanceId;
            _oldOrdinal = oldOrdinal; _newOrdinal = newOrdinal;
        }

        public override string Name => "Reorder action";
        protected override void ExecuteCore(DbConnection connection) => ActionInstanceRepository.MoveTo(connection, _sequenceId, _instanceId, _newOrdinal);
        protected override void UndoCore(DbConnection connection) => ActionInstanceRepository.MoveTo(connection, _sequenceId, _instanceId, _oldOrdinal);
    }

    /// <summary>Replaces one owned sequence atomically, preserving its root ID.</summary>
    public sealed class ReplaceActionSequenceContentsCommand : AuthoringCommandBase
    {
        private readonly ActionSequenceDefinition _oldSequence;
        private ActionSequenceDefinition _newSequence;

        public ReplaceActionSequenceContentsCommand(Func<DbConnection> conn,
            ActionSequenceDefinition oldSequence, ActionSequenceDefinition newSequence) : base(conn)
        {
            _oldSequence = oldSequence ?? throw new ArgumentNullException(nameof(oldSequence));
            _newSequence = newSequence ?? throw new ArgumentNullException(nameof(newSequence));
        }

        public override string Name => "Edit nested actions";
        public override string MergeKey => "sequence:" + _oldSequence.Id;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is ReplaceActionSequenceContentsCommand replace && replace._oldSequence.Id == _oldSequence.Id)
            {
                _newSequence = replace._newSequence;
                return true;
            }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection) => Replace(connection, _newSequence);

        protected override void UndoCore(DbConnection connection) => Replace(connection, _oldSequence);

        private static void Replace(DbConnection connection, ActionSequenceDefinition sequence)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ActionSequenceWriter.Sync(connection, transaction, sequence);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }
    }

    /// <summary>Appends a PhaseGoto to an Action node's sequence; undo removes it (deterministic instance id).</summary>
    public sealed class AddPhaseGotoCommand : AuthoringCommandBase
    {
        private readonly string _nodeId;
        private readonly string _exitId;
        private string _instanceId;

        public AddPhaseGotoCommand(Func<DbConnection> conn, string nodeId, string exitId) : base(conn)
        {
            _nodeId = nodeId; _exitId = exitId;
        }

        public override string Name => "Add exit action";

        protected override void ExecuteCore(DbConnection connection)
        {
            var sequenceId = AuthoringUndo.NodeActionSequenceId(connection, _nodeId);
            var ordinal = AuthoringUndo.NextInstanceOrdinal(connection, sequenceId);
            _instanceId = _nodeId + "-goto-" + ordinal;
            PhaseGraphRepository.AddPhaseGoto(connection, _nodeId, _exitId);
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_instanceId != null) PhaseGraphRepository.RemovePhaseGoto(connection, _instanceId);
        }
    }

    /// <summary>Appends a PhaseGoto to a decision option's sequence; undo removes it.</summary>
    public sealed class AddPhaseOptionGotoCommand : AuthoringCommandBase
    {
        private readonly string _optionId;
        private readonly string _exitId;
        private string _instanceId;

        public AddPhaseOptionGotoCommand(Func<DbConnection> conn, string optionId, string exitId) : base(conn)
        {
            _optionId = optionId; _exitId = exitId;
        }

        public override string Name => "Add exit action";

        protected override void ExecuteCore(DbConnection connection)
        {
            var sequenceId = AuthoringUndo.PhaseOptionSequenceId(connection, _optionId);
            var ordinal = AuthoringUndo.NextInstanceOrdinal(connection, sequenceId);
            _instanceId = _optionId + "-goto-" + ordinal;
            PhaseGraphRepository.AddPhaseGotoToOption(connection, _optionId, _exitId);
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_instanceId != null) PhaseGraphRepository.RemovePhaseGoto(connection, _instanceId);
        }
    }

    /// <summary>Appends a SessionGoto to a decision option's sequence (creates its unique port); undo removes instance + port + edges.</summary>
    public sealed class AddSessionGotoCommand : AuthoringCommandBase
    {
        private readonly string _nodeId;
        private readonly string _optionId;
        private readonly string _label;
        private string _instanceId;

        public AddSessionGotoCommand(Func<DbConnection> conn, string nodeId, string optionId, string label) : base(conn)
        {
            _nodeId = nodeId; _optionId = optionId; _label = label;
        }

        public override string Name => "Add session goto";

        protected override void ExecuteCore(DbConnection connection)
        {
            _instanceId = SessionDecisionRepository.AddSessionGoto(connection, _nodeId, _optionId, _label);
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_instanceId != null) SessionDecisionRepository.RemoveSessionGoto(connection, _instanceId);
        }
    }

    /// <summary>Removes one PhaseGoto instance; undo restores it at its exact ordinal.</summary>
    public sealed class RemovePhaseGotoCommand : AuthoringCommandBase
    {
        private readonly PhaseGotoSnapshot _snapshot;

        public RemovePhaseGotoCommand(Func<DbConnection> conn, PhaseGotoSnapshot snapshot) : base(conn)
        {
            _snapshot = snapshot;
        }

        public override string Name => "Remove exit action";

        protected override void ExecuteCore(DbConnection connection)
        {
            PhaseGraphRepository.RemovePhaseGoto(connection, _snapshot.InstanceId);
        }

        protected override void UndoCore(DbConnection connection)
        {
            AuthoringUndo.RestorePhaseGoto(connection, _snapshot);
        }
    }

    /// <summary>Removes one SessionGoto instance (cascade: port + edges); undo restores all three.</summary>
    public sealed class RemoveSessionGotoCommand : AuthoringCommandBase
    {
        private readonly SessionGotoSnapshot _snapshot;

        public RemoveSessionGotoCommand(Func<DbConnection> conn, SessionGotoSnapshot snapshot) : base(conn)
        {
            _snapshot = snapshot;
        }

        public override string Name => "Remove session goto";

        protected override void ExecuteCore(DbConnection connection)
        {
            SessionDecisionRepository.RemoveSessionGoto(connection, _snapshot.InstanceId);
        }

        protected override void UndoCore(DbConnection connection)
        {
            AuthoringUndo.RestoreSessionGoto(connection, _snapshot);
        }
    }

    /// <summary>Re-points a PhaseGoto at another exit (inline ComboBox).</summary>
    public sealed class SetPhaseGotoExitCommand : AuthoringCommandBase
    {
        private readonly string _instanceId;
        private readonly string _oldExitId;
        private readonly string _newExitId;

        public SetPhaseGotoExitCommand(Func<DbConnection> conn, string instanceId, string oldExitId, string newExitId) : base(conn)
        {
            _instanceId = instanceId; _oldExitId = oldExitId; _newExitId = newExitId;
        }

        public override string Name => "Change goto target";

        protected override void ExecuteCore(DbConnection connection) => PhaseGraphRepository.SetPhaseGotoExit(connection, _instanceId, _newExitId);
        protected override void UndoCore(DbConnection connection) => PhaseGraphRepository.SetPhaseGotoExit(connection, _instanceId, _oldExitId);
    }

    /// <summary>SessionGoto label edit (coalesced per keystroke burst; renames the projected port live).</summary>
    public sealed class SetSessionGotoLabelCommand : AuthoringCommandBase
    {
        private readonly string _instanceId;
        private readonly string _oldLabel;
        private string _newLabel;

        public SetSessionGotoLabelCommand(Func<DbConnection> conn, string instanceId, string oldLabel, string newLabel) : base(conn)
        {
            _instanceId = instanceId; _oldLabel = oldLabel; _newLabel = newLabel;
        }

        public override string Name => "Rename goto";
        public override string MergeKey => "gotolabel:" + _instanceId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is SetSessionGotoLabelCommand label) { _newLabel = label._newLabel; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection) => SessionDecisionRepository.SetSessionGotoLabel(connection, _instanceId, _newLabel);
        protected override void UndoCore(DbConnection connection) => SessionDecisionRepository.SetSessionGotoLabel(connection, _instanceId, _oldLabel);
    }

    // ==================================================================
    // Decision options
    // ==================================================================

    /// <summary>Adds a decision option row; undo removes it.</summary>
    public sealed class AddDecisionOptionCommand : AuthoringCommandBase
    {
        private readonly string _scope;
        private readonly string _nodeId;
        private readonly string _optionId;
        private readonly string _label;
        private readonly string _sequenceId;

        public AddDecisionOptionCommand(Func<DbConnection> conn, string scope, string nodeId, string optionId, string label, string sequenceId)
            : base(conn) { _scope = scope; _nodeId = nodeId; _optionId = optionId; _label = label; _sequenceId = sequenceId; }

        public override string Name => "Add option";

        protected override void ExecuteCore(DbConnection connection)
        {
            if (_scope == "session") SessionDecisionRepository.AddOption(connection, _nodeId, _optionId, _label, _sequenceId);
            else PhaseDecisionRepository.AddOption(connection, _nodeId, _optionId, _label, _sequenceId);
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_scope == "session") SessionDecisionRepository.RemoveOption(connection, _nodeId, _optionId);
            else PhaseDecisionRepository.RemoveOption(connection, _nodeId, _optionId);
        }
    }

    /// <summary>Removes a decision option; undo restores the option, its owned gotos, ports and edges.</summary>
    public sealed class RemoveDecisionOptionCommand : AuthoringCommandBase
    {
        private readonly string _scope;
        private readonly object _snapshot; // SessionOptionSnapshot | PhaseOptionSnapshot

        public RemoveDecisionOptionCommand(Func<DbConnection> conn, string scope, object snapshot) : base(conn)
        {
            _scope = scope; _snapshot = snapshot;
        }

        public override string Name => "Remove option";

        protected override void ExecuteCore(DbConnection connection)
        {
            if (_scope == "session")
            {
                var snapshot = (SessionOptionSnapshot)_snapshot;
                SessionDecisionRepository.RemoveOption(connection, snapshot.NodeId, snapshot.OptionId);
            }
            else
            {
                var snapshot = (PhaseOptionSnapshot)_snapshot;
                PhaseDecisionRepository.RemoveOption(connection, snapshot.NodeId, snapshot.OptionId);
            }
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_scope == "session") AuthoringUndo.RestoreSessionOption(connection, (SessionOptionSnapshot)_snapshot);
            else AuthoringUndo.RestorePhaseOption(connection, (PhaseOptionSnapshot)_snapshot);
        }
    }

    /// <summary>Decision option label edit (coalesced per keystroke burst).</summary>
    public sealed class RenameOptionCommand : AuthoringCommandBase
    {
        private readonly string _scope;
        private readonly string _optionId;
        private readonly string _oldLabel;
        private string _newLabel;

        public RenameOptionCommand(Func<DbConnection> conn, string scope, string optionId, string oldLabel, string newLabel)
            : base(conn) { _scope = scope; _optionId = optionId; _oldLabel = oldLabel; _newLabel = newLabel; }

        public override string Name => "Rename option";
        public override string MergeKey => "optionlabel:" + _optionId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is RenameOptionCommand rename) { _newLabel = rename._newLabel; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection)
        {
            if (_scope == "session") SessionDecisionRepository.RenameOption(connection, _optionId, _newLabel);
            else PhaseDecisionRepository.RenameOption(connection, _optionId, _newLabel);
        }

        protected override void UndoCore(DbConnection connection)
        {
            if (_scope == "session") SessionDecisionRepository.RenameOption(connection, _optionId, _oldLabel);
            else PhaseDecisionRepository.RenameOption(connection, _optionId, _oldLabel);
        }
    }

    // ==================================================================
    // Reuse operations (Copy Session / Duplicate Phase / Make Unique)
    // ==================================================================

    /// <summary>Copy Session: one command; undo deletes the copy, redo re-writes it (same ids).</summary>
    public sealed class CopySessionCommand : AuthoringCommandBase
    {
        private readonly SessionDefinition _clone;
        private readonly List<(string NodeId, double X, double Y)> _layout;

        public CopySessionCommand(Func<DbConnection> conn, SessionDefinition clone, List<(string NodeId, double X, double Y)> layout)
            : base(conn) { _clone = clone; _layout = layout; }

        public override string Name => "Copy session";

        protected override void ExecuteCore(DbConnection connection)
        {
            ReuseWriter.WriteClonedSession(connection, _clone);
            foreach (var pair in _layout)
            {
                AuthoringLayoutRepository.SaveSessionNodePosition(connection, _clone.Id, pair.NodeId, pair.X, pair.Y);
            }
        }

        protected override void UndoCore(DbConnection connection)
        {
            SessionRepository.Delete(connection, _clone.Id);
        }
    }

    /// <summary>Duplicate Phase: one command; undo deletes the copy, redo re-writes it (same ids).</summary>
    public sealed class DuplicatePhaseCommand : AuthoringCommandBase
    {
        private readonly PhaseDefinition _clone;
        private readonly List<(string NodeId, double X, double Y)> _layout;

        public DuplicatePhaseCommand(Func<DbConnection> conn, PhaseDefinition clone, List<(string NodeId, double X, double Y)> layout)
            : base(conn) { _clone = clone; _layout = layout; }

        public override string Name => "Duplicate phase";

        protected override void ExecuteCore(DbConnection connection)
        {
            ReuseWriter.WriteClonedPhase(connection, _clone);
            foreach (var pair in _layout)
            {
                AuthoringLayoutRepository.SavePhaseNodePosition(connection, _clone.Id, pair.NodeId, pair.X, pair.Y);
            }
        }

        protected override void UndoCore(DbConnection connection)
        {
            PhaseRepository.Delete(connection, _clone.Id);
        }
    }

    /// <summary>
    /// Groups several commands so they undo as one operation (Ticket 18: the
    /// shared-port flow "Make Unique + apply the blocked exit edit" must undo
    /// as a single step). Undo runs the inner commands in reverse order.
    /// </summary>
    public sealed class CompositeCommand : IAuthoringCommand
    {
        private readonly IAuthoringCommand[] _inner;

        public CompositeCommand(string name, params IAuthoringCommand[] inner)
        {
            Name = name;
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Name { get; }
        public string MergeKey => null;
        public bool Merge(IAuthoringCommand incoming) => false;

        public void Execute()
        {
            foreach (var command in _inner) command.Execute();
        }

        public void Undo()
        {
            for (var i = _inner.Length - 1; i >= 0; i--) _inner[i].Undo();
        }
    }

    /// <summary>
    /// Make Unique: undo restores the placement to its shared phase (port exit
    /// mappings included) and removes the clone as one operation. Redo re-runs
    /// the detach (deterministic clone ids).
    /// </summary>
    public sealed class MakeUniqueCommand : AuthoringCommandBase
    {
        private readonly PhaseDefinition _sharedPhase;
        private readonly string _placementNodeId;
        private readonly string _newPhaseId;
        private readonly Dictionary<string, string> _portToOldExit;

        public MakeUniqueCommand(Func<DbConnection> conn, PhaseDefinition sharedPhase, string placementNodeId,
            string newPhaseId, Dictionary<string, string> portToOldExit) : base(conn)
        {
            _sharedPhase = sharedPhase; _placementNodeId = placementNodeId;
            _newPhaseId = newPhaseId; _portToOldExit = portToOldExit;
        }

        public override string Name => "Make unique";

        protected override void ExecuteCore(DbConnection connection)
        {
            MakeUniqueRepository.MakeUnique(connection, _sharedPhase, _placementNodeId, _newPhaseId);
        }

        protected override void UndoCore(DbConnection connection)
        {
            AuthoringUndo.RevertPlacement(connection, _placementNodeId, _sharedPhase.Id, _portToOldExit);
            PhaseRepository.Delete(connection, _newPhaseId);
        }
    }

    // =====================================================================
    // Milestone B authoring commands (Tickets 11/13/14).
    // =====================================================================

    /// <summary>Creates a catalog definition (SessionType/CardTag/Kink/Equipment/Capability).</summary>
    public sealed class CreateCatalogEntryCommand : AuthoringCommandBase
    {
        private readonly string _kind;
        private readonly string _id;
        private readonly string _title;

        public CreateCatalogEntryCommand(Func<DbConnection> conn, string kind, string id, string title) : base(conn)
        {
            _kind = kind; _id = id; _title = title;
        }

        public override string Name => "Create " + _kind;

        protected override void ExecuteCore(DbConnection connection)
        {
            switch (_kind)
            {
                case CatalogKinds.SessionType:
                    CatalogRepositories.CreateSessionType(connection, new SessionTypeDefinition { Id = _id, Title = _title });
                    break;
                case CatalogKinds.CardTag:
                    CatalogRepositories.CreateCardTag(connection, new CardTagDefinition { Id = _id, Title = _title });
                    break;
                case CatalogKinds.Kink:
                    CatalogRepositories.CreateKink(connection, new KinkDefinition { Id = _id, Title = _title });
                    break;
                case CatalogKinds.Equipment:
                    CatalogRepositories.CreateEquipment(connection, new EquipmentDefinition { Id = _id, Title = _title });
                    break;
                case CatalogKinds.SmartToyCapability:
                    CatalogRepositories.CreateSmartToyCapability(connection, new SmartToyCapabilityDefinition { Id = _id, Title = _title });
                    break;
                default:
                    throw new InvalidOperationException("Unknown catalog kind '" + _kind + "'.");
            }
        }

        protected override void UndoCore(DbConnection connection)
        {
            switch (_kind)
            {
                case CatalogKinds.SessionType: Sql.Execute(connection, null, "DELETE FROM session_type WHERE id = @id;", ("id", _id)); break;
                case CatalogKinds.CardTag: Sql.Execute(connection, null, "DELETE FROM card_tag_definition WHERE id = @id;", ("id", _id)); break;
                case CatalogKinds.Kink: Sql.Execute(connection, null, "DELETE FROM kink_definition WHERE id = @id;", ("id", _id)); break;
                case CatalogKinds.Equipment: Sql.Execute(connection, null, "DELETE FROM equipment_definition WHERE id = @id;", ("id", _id)); break;
                case CatalogKinds.SmartToyCapability: Sql.Execute(connection, null, "DELETE FROM smart_toy_capability_definition WHERE id = @id;", ("id", _id)); break;
            }
        }
    }

    /// <summary>Deletes an unreferenced catalog definition (usage blocking happens in the UI before push).</summary>
    public sealed class DeleteCatalogEntryCommand : AuthoringCommandBase
    {
        private readonly string _kind;
        private readonly string _id;
        private readonly string _title;

        public DeleteCatalogEntryCommand(Func<DbConnection> conn, string kind, string id, string title) : base(conn)
        {
            _kind = kind; _id = id; _title = title;
        }

        public override string Name => "Delete " + _kind;

        protected override void ExecuteCore(DbConnection connection)
        {
            switch (_kind)
            {
                case CatalogKinds.SessionType: Sql.Execute(connection, null, "DELETE FROM session_type WHERE id = @id;", ("id", _id)); break;
                case CatalogKinds.CardTag: Sql.Execute(connection, null, "DELETE FROM card_tag_definition WHERE id = @id;", ("id", _id)); break;
                case CatalogKinds.Kink: Sql.Execute(connection, null, "DELETE FROM kink_definition WHERE id = @id;", ("id", _id)); break;
                case CatalogKinds.Equipment: Sql.Execute(connection, null, "DELETE FROM equipment_definition WHERE id = @id;", ("id", _id)); break;
                case CatalogKinds.SmartToyCapability: Sql.Execute(connection, null, "DELETE FROM smart_toy_capability_definition WHERE id = @id;", ("id", _id)); break;
            }
        }

        protected override void UndoCore(DbConnection connection)
        {
            // Re-creates the row; the definition was unreferenced when deleted,
            // and undo restores exactly the same stable id/title.
            switch (_kind)
            {
                case CatalogKinds.SessionType:
                    CatalogRepositories.CreateSessionType(connection, new SessionTypeDefinition { Id = _id, Title = _title });
                    break;
                case CatalogKinds.CardTag:
                    CatalogRepositories.CreateCardTag(connection, new CardTagDefinition { Id = _id, Title = _title });
                    break;
                case CatalogKinds.Kink:
                    CatalogRepositories.CreateKink(connection, new KinkDefinition { Id = _id, Title = _title });
                    break;
                case CatalogKinds.Equipment:
                    CatalogRepositories.CreateEquipment(connection, new EquipmentDefinition { Id = _id, Title = _title });
                    break;
                case CatalogKinds.SmartToyCapability:
                    CatalogRepositories.CreateSmartToyCapability(connection, new SmartToyCapabilityDefinition { Id = _id, Title = _title });
                    break;
            }
        }
    }

    /// <summary>Renames a catalog definition.</summary>
    public sealed class RenameCatalogEntryCommand : AuthoringCommandBase
    {
        private readonly string _kind;
        private readonly string _id;
        private readonly string _oldTitle;
        private string _newTitle;

        public RenameCatalogEntryCommand(Func<DbConnection> conn, string kind, string id, string oldTitle, string newTitle) : base(conn)
        {
            _kind = kind; _id = id; _oldTitle = oldTitle; _newTitle = newTitle;
        }

        public override string Name => "Rename " + _kind;
        public override string MergeKey => "catalogrename:" + _kind + ":" + _id;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is RenameCatalogEntryCommand rename) { _newTitle = rename._newTitle; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection) => Rename(connection, _newTitle);

        protected override void UndoCore(DbConnection connection) => Rename(connection, _oldTitle);

        private void Rename(DbConnection connection, string title)
        {
            switch (_kind)
            {
                case CatalogKinds.SessionType: CatalogRepositories.RenameSessionType(connection, _id, title); break;
                case CatalogKinds.CardTag: CatalogRepositories.RenameCardTag(connection, _id, title); break;
                case CatalogKinds.Kink: Sql.Execute(connection, null, "UPDATE kink_definition SET title = @t WHERE id = @id;", ("t", (object)title ?? DBNull.Value), ("id", _id)); break;
                case CatalogKinds.Equipment: Sql.Execute(connection, null, "UPDATE equipment_definition SET title = @t WHERE id = @id;", ("t", (object)title ?? DBNull.Value), ("id", _id)); break;
                case CatalogKinds.SmartToyCapability: Sql.Execute(connection, null, "UPDATE smart_toy_capability_definition SET title = @t WHERE id = @id;", ("t", (object)title ?? DBNull.Value), ("id", _id)); break;
            }
        }
    }

    /// <summary>Atomically edits the fields of one stable-ID catalog row.</summary>
    public sealed class UpdateCatalogEntryCommand : AuthoringCommandBase
    {
        private readonly CatalogEntryEdit _oldValue;
        private readonly CatalogEntryEdit _newValue;

        public UpdateCatalogEntryCommand(Func<DbConnection> conn, CatalogEntryEdit oldValue, CatalogEntryEdit newValue)
            : base(conn)
        {
            _oldValue = oldValue ?? throw new ArgumentNullException(nameof(oldValue));
            _newValue = newValue ?? throw new ArgumentNullException(nameof(newValue));
        }

        public override string Name => "Edit catalog entry";

        protected override void ExecuteCore(DbConnection connection) => Apply(connection, _newValue);

        protected override void UndoCore(DbConnection connection) => Apply(connection, _oldValue);

        private static void Apply(DbConnection connection, CatalogEntryEdit value)
        {
            switch (value.Kind)
            {
                case CatalogKinds.SessionType:
                    CatalogRepositories.UpdateSessionType(connection, new SessionTypeDefinition
                    {
                        Id = value.Id, Title = value.Title, SortOrder = value.SortOrder,
                        RequiredCapabilityIds = new List<string>(value.RequiredCapabilityIds ?? new List<string>())
                    });
                    break;
                case CatalogKinds.CardTag:
                    CatalogRepositories.RenameCardTag(connection, value.Id, value.Title);
                    break;
                case CatalogKinds.Kink:
                    CatalogRepositories.UpdateKink(connection, new KinkDefinition
                    {
                        Id = value.Id, Title = value.Title, Description = value.Description, SortOrder = value.SortOrder
                    });
                    break;
                case CatalogKinds.Equipment:
                    CatalogRepositories.UpdateEquipment(connection, new EquipmentDefinition
                    {
                        Id = value.Id, Title = value.Title, Category = value.Category, SortOrder = value.SortOrder
                    });
                    break;
                case CatalogKinds.SmartToyCapability:
                    CatalogRepositories.UpdateSmartToyCapability(connection, new SmartToyCapabilityDefinition
                    {
                        Id = value.Id, Title = value.Title, Category = value.Category, SortOrder = value.SortOrder
                    });
                    break;
                default:
                    throw new InvalidOperationException("Unknown catalog kind '" + value.Kind + "'.");
            }
        }
    }

    /// <summary>Database-neutral snapshot used by the catalog editor command.</summary>
    public sealed class CatalogEntryEdit
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public int SortOrder { get; set; }
        public List<string> RequiredCapabilityIds { get; set; } = new List<string>();
    }

    /// <summary>Creates a Card with the default owned sequence (WaitForContinue + IncrementProgress +10).</summary>
    public sealed class CreateCardCommand : AuthoringCommandBase
    {
        private readonly string _id;
        private readonly string _title;

        public CreateCardCommand(Func<DbConnection> conn, string id, string title) : base(conn)
        {
            _id = id; _title = title;
        }

        public override string Name => "Create card";

        protected override void ExecuteCore(DbConnection connection)
        {
            CardRepository.Create(connection, new CardDefinition { Id = _id, Title = _title });
        }

        protected override void UndoCore(DbConnection connection)
        {
            CardRepository.Delete(connection, _id);
        }
    }

    /// <summary>Deletes a card and its owned sequence; undo recreates the captured definition.</summary>
    public sealed class DeleteCardCommand : AuthoringCommandBase
    {
        private readonly CardDefinition _snapshot;

        public DeleteCardCommand(Func<DbConnection> conn, string cardId) : base(conn)
        {
            // Capture the full definition at construction (undo restores it verbatim).
            _snapshot = null;
            using (var connection = conn())
            {
                string title = null, body = null, sequenceId = null;
                Sql.QueryAll(connection, "SELECT title, body_text, action_sequence_id FROM card WHERE id = @id;",
                    reader =>
                    {
                        title = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        body = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        sequenceId = reader.IsDBNull(2) ? null : reader.GetString(2);
                    },
                    ("id", cardId));
                if (sequenceId == null) throw new InvalidOperationException($"Card '{cardId}' not found.");

                _snapshot = new CardDefinition { Id = cardId, Title = title, BodyText = body };
                _snapshot.CardTagIds.AddRange(IdsOf(connection, "SELECT tag_id FROM card_tag WHERE card_id = @id;", cardId));
                _snapshot.KinkIds.AddRange(IdsOf(connection, "SELECT kink_id FROM card_kink WHERE card_id = @id;", cardId));
                _snapshot.RequiredEquipmentIds.AddRange(IdsOf(connection, "SELECT equipment_id FROM card_required_equipment WHERE card_id = @id;", cardId));
                _snapshot.RequiredCapabilityIds.AddRange(IdsOf(connection, "SELECT capability_id FROM card_required_smart_toy_capability WHERE card_id = @id;", cardId));
                _snapshot.Sequence = GameContentSnapshotLoader.LoadSequence(connection, sequenceId);
            }
        }

        private static List<string> IdsOf(DbConnection connection, string sql, string cardId)
        {
            var ids = new List<string>();
            Sql.QueryAll(connection, sql, reader => ids.Add(reader.GetString(0)), ("id", cardId));
            return ids;
        }

        public override string Name => "Delete card";

        protected override void ExecuteCore(DbConnection connection)
        {
            CardRepository.Delete(connection, _snapshot.Id);
        }

        protected override void UndoCore(DbConnection connection)
        {
            CardRepository.Create(connection, _snapshot);
        }
    }

    /// <summary>Duplicates a card (relations + instances to new ids; Resources stay shared).</summary>
    public sealed class DuplicateCardCommand : AuthoringCommandBase
    {
        private readonly string _sourceCardId;
        private readonly string _newCardId;
        private readonly string _title;

        public DuplicateCardCommand(Func<DbConnection> conn, string sourceCardId, string newCardId, string title) : base(conn)
        {
            _sourceCardId = sourceCardId; _newCardId = newCardId; _title = title;
        }

        public override string Name => "Duplicate card";

        protected override void ExecuteCore(DbConnection connection)
        {
            CardRepository.Duplicate(connection, _sourceCardId, _newCardId, _title);
        }

        protected override void UndoCore(DbConnection connection)
        {
            CardRepository.Delete(connection, _newCardId);
        }
    }

    /// <summary>Renames a card (coalesced).</summary>
    public sealed class RenameCardCommand : AuthoringCommandBase
    {
        private readonly string _cardId;
        private readonly string _oldTitle;
        private string _newTitle;

        public RenameCardCommand(Func<DbConnection> conn, string cardId, string oldTitle, string newTitle) : base(conn)
        {
            _cardId = cardId; _oldTitle = oldTitle; _newTitle = newTitle;
        }

        public override string Name => "Rename card";
        public override string MergeKey => "cardtitle:" + _cardId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is RenameCardCommand rename) { _newTitle = rename._newTitle; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection) => CardRepository.Rename(connection, _cardId, _newTitle);
        protected override void UndoCore(DbConnection connection) => CardRepository.Rename(connection, _cardId, _oldTitle);
    }

    /// <summary>Sets the card body text (coalesced).</summary>
    public sealed class SetCardBodyCommand : AuthoringCommandBase
    {
        private readonly string _cardId;
        private readonly string _oldBody;
        private string _newBody;

        public SetCardBodyCommand(Func<DbConnection> conn, string cardId, string oldBody, string newBody) : base(conn)
        {
            _cardId = cardId; _oldBody = oldBody; _newBody = newBody;
        }

        public override string Name => "Edit card body";
        public override string MergeKey => "cardbody:" + _cardId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is SetCardBodyCommand body) { _newBody = body._newBody; return true; }
            return false;
        }

        protected override void ExecuteCore(DbConnection connection) => CardRepository.SetBody(connection, _cardId, _newBody);
        protected override void UndoCore(DbConnection connection) => CardRepository.SetBody(connection, _cardId, _oldBody);
    }

    /// <summary>Replaces the card's four relation lists atomically; undo restores the captured originals.</summary>
    public sealed class SetCardRelationsCommand : AuthoringCommandBase
    {
        private readonly CardDefinition _card;
        private readonly List<string> _oldTags;
        private readonly List<string> _oldKinks;
        private readonly List<string> _oldEquipment;
        private readonly List<string> _oldCapabilities;
        private List<string> _newTags;
        private List<string> _newKinks;
        private List<string> _newEquipment;
        private List<string> _newCapabilities;

        public SetCardRelationsCommand(Func<DbConnection> conn, CardDefinition card,
            IReadOnlyList<string> oldTags, IReadOnlyList<string> oldKinks,
            IReadOnlyList<string> oldEquipment, IReadOnlyList<string> oldCapabilities,
            IReadOnlyList<string> newTags, IReadOnlyList<string> newKinks,
            IReadOnlyList<string> newEquipment, IReadOnlyList<string> newCapabilities) : base(conn)
        {
            _card = card;
            _oldTags = new List<string>(oldTags ?? new List<string>());
            _oldKinks = new List<string>(oldKinks ?? new List<string>());
            _oldEquipment = new List<string>(oldEquipment ?? new List<string>());
            _oldCapabilities = new List<string>(oldCapabilities ?? new List<string>());
            _newTags = new List<string>(newTags ?? new List<string>());
            _newKinks = new List<string>(newKinks ?? new List<string>());
            _newEquipment = new List<string>(newEquipment ?? new List<string>());
            _newCapabilities = new List<string>(newCapabilities ?? new List<string>());
        }

        public override string Name => "Edit card relations";
        public override string MergeKey => "cardrelations:" + _card.Id;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (!(incoming is SetCardRelationsCommand relations)) return false;
            _newTags = new List<string>(relations._newTags);
            _newKinks = new List<string>(relations._newKinks);
            _newEquipment = new List<string>(relations._newEquipment);
            _newCapabilities = new List<string>(relations._newCapabilities);
            return true;
        }

        protected override void ExecuteCore(DbConnection connection) => Apply(connection, _newTags, _newKinks, _newEquipment, _newCapabilities);
        protected override void UndoCore(DbConnection connection) => Apply(connection, _oldTags, _oldKinks, _oldEquipment, _oldCapabilities);

        private void Apply(DbConnection connection, List<string> tags, List<string> kinks, List<string> equipment, List<string> capabilities)
        {
            var snapshot = new CardDefinition
            {
                Id = _card.Id,
                Title = _card.Title,
                BodyText = _card.BodyText,
                CardTagIds = tags,
                KinkIds = kinks,
                RequiredEquipmentIds = equipment,
                RequiredCapabilityIds = capabilities,
            };
            CardRepository.ReplaceRelations(connection, snapshot);
        }
    }

    /// <summary>Replaces the session's card weighting (coalesced).</summary>
    public sealed class SetSessionWeightingCommand : AuthoringCommandBase
    {        private readonly string _sessionId;
        private readonly SessionCardWeightingDefinition _oldWeighting;
        private SessionCardWeightingDefinition _newWeighting;

        public SetSessionWeightingCommand(Func<DbConnection> conn, string sessionId,
            SessionCardWeightingDefinition oldWeighting, SessionCardWeightingDefinition newWeighting) : base(conn)
        {
            _sessionId = sessionId; _oldWeighting = Clone(oldWeighting); _newWeighting = Clone(newWeighting);
        }

        public override string Name => "Edit card weighting";
        public override string MergeKey => "sessionweighting:" + _sessionId;

        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is SetSessionWeightingCommand weighting) { _newWeighting = Clone(weighting._newWeighting); return true; }
            return false;
        }

        private static SessionCardWeightingDefinition Clone(SessionCardWeightingDefinition source)
        {
            return new SessionCardWeightingDefinition
            {
                LoveBase = source.LoveBase,
                LoveHappinessGain = source.LoveHappinessGain,
                LikeBase = source.LikeBase,
                LikeHappinessGain = source.LikeHappinessGain,
                TortureBase = source.TortureBase,
                TortureUnhappinessGain = source.TortureUnhappinessGain,
            };
        }

        protected override void ExecuteCore(DbConnection connection) => SessionRepository.ReplaceCardWeighting(connection, _sessionId, _newWeighting);
        protected override void UndoCore(DbConnection connection) => SessionRepository.ReplaceCardWeighting(connection, _sessionId, _oldWeighting);
    }

    /// <summary>
    /// Replaces a card's entire owned Action sequence with a new one (the
    /// buffered Card editor's Save path). Id-stable by design: the buffer
    /// preserves original instance ids, so undo restores the exact rows.
    /// </summary>
    public sealed class ReplaceCardSequenceCommand : AuthoringCommandBase
    {
        private readonly string _cardId;
        private readonly ActionSequenceDefinition _oldSequence;
        private readonly ActionSequenceDefinition _newSequence;

        public ReplaceCardSequenceCommand(Func<DbConnection> conn, string cardId,
            ActionSequenceDefinition oldSequence, ActionSequenceDefinition newSequence) : base(conn)
        {
            _cardId = cardId;
            _oldSequence = oldSequence ?? throw new ArgumentNullException(nameof(oldSequence));
            _newSequence = newSequence ?? throw new ArgumentNullException(nameof(newSequence));
        }

        public override string Name => "Edit card actions";

        protected override void ExecuteCore(DbConnection connection)
        {
            Replace(connection, _newSequence);
        }

        protected override void UndoCore(DbConnection connection)
        {
            Replace(connection, _oldSequence);
        }

        private void Replace(DbConnection connection, ActionSequenceDefinition sequence)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ActionSequenceWriter.Sync(connection, transaction, sequence);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }
    }

    /// <summary>Stable catalog-kind keys shared by the catalog commands.</summary>
    public static class CatalogKinds
    {
        public const string SessionType = "session-type";
        public const string CardTag = "card-tag";
        public const string Kink = "kink";
        public const string Equipment = "equipment";
        public const string SmartToyCapability = "smart-toy-capability";
    }
}
