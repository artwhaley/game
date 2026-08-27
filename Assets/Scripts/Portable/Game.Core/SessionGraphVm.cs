using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// The high-level Session graph VM (Tickets 08-09): composes reusable Phases
    /// via PhaseReference nodes, resolves PhaseGoto/SessionGoto through the
    /// session graph's projected sockets, and drives the explicit continuation
    /// stack for RETURN. T08 scope: Start / PhaseReference / End plus
    /// PhaseGoto / Return / EndSession; T09 adds SessionDecision / SessionGoto.
    ///
    /// Flow-control rules (execution semantics): every PhaseReference entrance
    /// creates a fresh PhaseRun (even for the same Phase deeper on the stack);
    /// RETURN restores the exact saved frame (locus, PhaseRun, chain, card);
    /// temperatures are session-global and stay current across transfers;
    /// EndSession clears the entire stack.
    /// </summary>
    public sealed class SessionGraphVm
    {
        private readonly GameContentDefinition _content;
        private readonly ContentCatalog _catalog;
        private readonly CoreServices _services;
        private readonly BackgroundActionTracker _tracker;
        private readonly PhaseRunRngFactory _rngFactory;
        private readonly SessionDefinition _session;
        private readonly ActionExecutor _executor;

        private readonly Dictionary<string, GraphNodeDefinition> _nodesById;
        private readonly Dictionary<string, GraphOutputDefinition> _outputsById;
        private readonly Dictionary<string, string> _edgeFromOutput;

        private readonly ContinuationStack _stack = new ContinuationStack();

        private GraphNodeDefinition _sessionNode;
        private PhaseRun _activeRun;
        private PhaseGraphVm _activePhaseVm;

        /// <summary>Set by RETURN: the next loop iteration resumes this saved frame instead of advancing fresh.</summary>
        private ContinuationFrame _pendingResume;

        public event Action<string> SessionNodeChanged;      // node id
        public event Action<string> PhaseEntered;            // phase id (forwarded)
        public event Action<string> PhaseNodeChanged;        // node id (forwarded)
        public event Action<CardDefinition> CardStarted;
        public event Action<CardDefinition> CardFinished;
        public event Action<VariableCheckNodeDefinition, float, bool> VariableCheckEvaluated;
        public event Action<string> RuntimeError;
        public event Action SessionCompleted;

        public SessionGraphVm(
            GameContentDefinition content, string sessionId, CoreServices services,
            BackgroundActionTracker tracker, PhaseRunRngFactory rngFactory)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _catalog = new ContentCatalog(content);
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _rngFactory = rngFactory ?? new PhaseRunRngFactory();
            _session = _catalog.SessionById(sessionId);
            _executor = new ActionExecutor(_tracker);

            if (_session.Graph == null) throw new InvalidOperationException($"Session '{sessionId}' has no graph.");

            _nodesById = new Dictionary<string, GraphNodeDefinition>();
            _outputsById = new Dictionary<string, GraphOutputDefinition>();
            _edgeFromOutput = new Dictionary<string, string>();

            var startCount = 0;
            foreach (var node in _session.Graph.Nodes)
            {
                if (node == null) throw new InvalidOperationException($"Session '{sessionId}' contains a null node row.");
                if (string.IsNullOrEmpty(node.Id)) throw new InvalidOperationException($"Session '{sessionId}' has a node with no id.");
                if (node is SessionStartNodeDefinition) startCount++;
                _nodesById[node.Id] = node;

                foreach (var output in node.Outputs)
                {
                    if (output == null || string.IsNullOrEmpty(output.Id))
                    {
                        throw new InvalidOperationException($"Session node '{node.Id}' has a null/empty output.");
                    }
                    _outputsById[output.Id] = output;
                }
            }

            if (startCount != 1)
            {
                throw new InvalidOperationException($"Session '{sessionId}' must have exactly one Start node (found {startCount}).");
            }

            foreach (var edge in _session.Graph.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.SourceOutputId) || string.IsNullOrEmpty(edge.TargetNodeId))
                {
                    throw new InvalidOperationException($"Session '{sessionId}' has a malformed edge row.");
                }
                if (!_outputsById.ContainsKey(edge.SourceOutputId))
                {
                    throw new InvalidOperationException($"Session '{sessionId}' edge '{edge.Id}' references unknown output '{edge.SourceOutputId}'.");
                }
                if (!_nodesById.ContainsKey(edge.TargetNodeId))
                {
                    throw new InvalidOperationException($"Session '{sessionId}' edge '{edge.Id}' references unknown target node '{edge.TargetNodeId}'.");
                }
                _edgeFromOutput[edge.SourceOutputId] = edge.TargetNodeId;
            }
        }

        public bool IsComplete { get; private set; }

        /// <summary>Current session graph node; null until the first advance.</summary>
        public string CurrentSessionNodeId => _sessionNode?.Id;

        public int ContinuationDepth => _stack.Count;

        /// <summary>
        /// Advances the whole session one user-paced step: runs at most one card
        /// (through whatever phase/transfer/return chain that involves), then
        /// yields. Fails loudly on runtime content errors.
        /// </summary>
        public async Task<SessionAdvanceResult> AdvanceAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (IsComplete)
            {
                return SessionAdvanceResult.SessionCompleted;
            }

            try
            {
                return await AdvanceCoreAsync(context, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                RuntimeError?.Invoke(ex.Message);
                return SessionAdvanceResult.Error(ex.Message);
            }
        }

        private async Task<SessionAdvanceResult> AdvanceCoreAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            var steps = 0;
            while (!IsComplete)
            {
                if (++steps > PhaseGraphVm.MaxStepsPerAdvance)
                {
                    throw new InvalidOperationException(
                        $"Session '{_session.Id}': loop guard exceeded {PhaseGraphVm.MaxStepsPerAdvance} session steps in one Advance. No silent gameplay loop.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (_pendingResume != null)
                {
                    var frame = _pendingResume;
                    _pendingResume = null;
                    var resumeResult = await _activePhaseVm.ResumeFromContinuationAsync(
                        _activeRun, frame.GraphLocus, frame.Chain, context, cancellationToken);
                    var resumed = await HandlePhaseResultAsync(resumeResult, context, cancellationToken);
                    if (resumed != null) return resumed;
                }
                else if (_activeRun != null)
                {
                    var phaseResult = await _activePhaseVm.AdvanceAsync(_activeRun, context, cancellationToken);
                    var result = await HandlePhaseResultAsync(phaseResult, context, cancellationToken);
                    if (result != null) return result;
                }
                else
                {
                    await StepSessionNodeAsync(context, cancellationToken);
                }
            }

            return SessionAdvanceResult.SessionCompleted;
        }

        /// <summary>Routes a phase advance result; returns a session result to yield, or null to keep looping.</summary>
        private async Task<SessionAdvanceResult> HandlePhaseResultAsync(
            PhaseAdvanceResult phaseResult, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            switch (phaseResult.Outcome)
            {
                case PhaseAdvanceOutcome.CardExecuted:
                    return SessionAdvanceResult.CardExecuted(phaseResult.Card);

                case PhaseAdvanceOutcome.YieldedForCard:
                    return SessionAdvanceResult.YieldedForCard;

                case PhaseAdvanceOutcome.Error:
                    return SessionAdvanceResult.Error(phaseResult.ErrorMessage);

                case PhaseAdvanceOutcome.Completed:
                    throw new InvalidOperationException(
                        $"Phase '{_activeRun.PhaseId}' completed without a control transfer (dead end or missing GOTO).");

                case PhaseAdvanceOutcome.Transferred:
                    await HandleTransferAsync(phaseResult.Transfer, context, cancellationToken);
                    return null;

                default:
                    throw new InvalidOperationException($"Unknown phase outcome '{phaseResult.Outcome}'.");
            }
        }

        // ---------- session node stepping ----------

        private async Task StepSessionNodeAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (_sessionNode == null)
            {
                _sessionNode = FindStart();
                SessionNodeChanged?.Invoke(_sessionNode.Id);
            }

            switch (_sessionNode)
            {
                case SessionStartNodeDefinition startNode:
                    _sessionNode = FollowNormal(startNode);
                    SessionNodeChanged?.Invoke(_sessionNode.Id);
                    break;

                case PhaseReferenceNodeDefinition reference:
                    await EnterPhaseAsync(reference, context, cancellationToken);
                    break;

                case SessionEndNodeDefinition endNode:
                    IsComplete = true;
                    _stack.Clear();
                    SessionCompleted?.Invoke();
                    break;

                case SessionDecisionNodeDefinition decision:
                    await EnterDecisionAsync(decision, context, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Session '{_session.Id}': unknown session node type '{_sessionNode.GetType().Name}'.");
            }
        }

        /// <summary>
        /// Creates a fresh PhaseRun (per-entrance rule) and installs it as the
        /// active run. The session loop drives it on the next iteration (its first
        /// AdvanceAsync enters the Entry node).
        /// </summary>
        private Task EnterPhaseAsync(PhaseReferenceNodeDefinition reference, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            var run = new PhaseRun(reference.Id, reference.PhaseId, _rngFactory.Create());
            _activeRun = run;
            _activePhaseVm = new PhaseGraphVm(_content, _catalog.PhaseById(reference.PhaseId), _services, _tracker);
            WirePhaseEvents(_activePhaseVm);
            return Task.CompletedTask;
        }

        private async Task EnterDecisionAsync(SessionDecisionNodeDefinition decision, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "SessionDecision nodes land in Ticket 09; content must not author them before then.");
        }

        // ---------- transfer resolution ----------

        private async Task HandleTransferAsync(ActionExecutionResult transfer, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            switch (transfer.Transfer)
            {
                case ActionTransfer.PhaseGoto:
                    await ResolvePhaseGotoAsync(transfer, context, cancellationToken);
                    break;

                case ActionTransfer.SessionGoto:
                    await ResolveSessionGotoAsync(transfer, context, cancellationToken);
                    break;

                case ActionTransfer.Return:
                    await HandleReturnAsync(context, cancellationToken);
                    break;

                case ActionTransfer.EndSession:
                    IsComplete = true;
                    _stack.Clear();
                    SessionCompleted?.Invoke();
                    break;

                default:
                    throw new InvalidOperationException("Session VM received an unknown transfer.");
            }
        }

        /// <summary>
        /// PhaseGoto: exit must belong to the active Phase; push the continuation
        /// (locus + PhaseRun + chain); resolve through the current placement's
        /// projected PhaseExit socket; require a wired session edge; enter target.
        /// </summary>
        private async Task ResolvePhaseGotoAsync(ActionExecutionResult transfer, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (_activeRun == null)
            {
                throw new InvalidOperationException($"PhaseGoto '{transfer.PhaseExitId}' fired with no active PhaseRun.");
            }

            var phase = _catalog.PhaseById(_activeRun.PhaseId);
            var exitValid = false;
            foreach (var exit in phase.Exits)
            {
                if (exit.Id == transfer.PhaseExitId) { exitValid = true; break; }
            }
            if (!exitValid)
            {
                throw new InvalidOperationException(
                    $"PhaseGoto references exit '{transfer.PhaseExitId}' which does not belong to active Phase '{_activeRun.PhaseId}'.");
            }

            // Save the continuation BEFORE transferring.
            _stack.Push(new ContinuationFrame(_activeRun.CurrentNode, _activeRun, transfer.Continuation, null));

            // Resolve through the current placement's projected socket.
            var placement = FindPlacement(_activeRun.PlacementNodeId);
            if (placement == null)
            {
                throw new InvalidOperationException(
                    $"Session '{_session.Id}': no PhaseReference node '{_activeRun.PlacementNodeId}' for the active PhaseRun.");
            }

            var socketId = FindProjectedSocket(placement, transfer.PhaseExitId);
            if (socketId == null)
            {
                throw new InvalidOperationException(
                    $"Unwired phase exit: Phase '{_activeRun.PhaseId}' exit '{transfer.PhaseExitId}' is not projected on placement '{placement.Id}' of Session '{_session.Id}'.");
            }

            if (!_edgeFromOutput.TryGetValue(socketId, out var targetNodeId))
            {
                throw new InvalidOperationException(
                    $"Unwired phase exit: projected socket '{socketId}' (exit '{transfer.PhaseExitId}') of Session '{_session.Id}' has no outgoing edge.");
            }

            // Transfer: the phase run is now suspended on the stack.
            _activeRun = null;
            _activePhaseVm = null;
            _sessionNode = _nodesById[targetNodeId];
            SessionNodeChanged?.Invoke(_sessionNode.Id);
        }

        /// <summary>SessionGoto (Ticket 09 surface): transfer along the instance's own session output port.</summary>
        private async Task ResolveSessionGotoAsync(ActionExecutionResult transfer, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "SessionGoto lands in Ticket 09; content must not author it before then.");
        }

        /// <summary>
        /// Return: pop the newest continuation; restore the exact frame (locus,
        /// PhaseRun with progress/RNG/history, chain). Temperatures stay current
        /// because they live in the shared context, untouched by transfers.
        /// Empty stack is a clear runtime error. The session loop resumes the
        /// frame via ResumeFromContinuationAsync on the next iteration.
        /// </summary>
        private Task HandleReturnAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            var frame = _stack.Pop(); // throws on empty with a clear message

            _activeRun = frame.PhaseRun;
            _activePhaseVm = new PhaseGraphVm(_content, _catalog.PhaseById(frame.PhaseRun.PhaseId), _services, _tracker);
            WirePhaseEvents(_activePhaseVm);
            _pendingResume = frame;
            return Task.CompletedTask;
        }

        // ---------- graph helpers ----------

        private PhaseReferenceNodeDefinition FindPlacement(string placementNodeId)
        {
            return _nodesById.TryGetValue(placementNodeId ?? "", out var node)
                ? node as PhaseReferenceNodeDefinition
                : null;
        }

        private string FindProjectedSocket(PhaseReferenceNodeDefinition placement, string exitId)
        {
            foreach (var output in placement.Outputs)
            {
                if (output.Kind == GraphPortKind.PhaseExit && output.PhaseExitId == exitId)
                {
                    return output.Id;
                }
            }
            return null;
        }

        private SessionStartNodeDefinition FindStart()
        {
            foreach (var node in _session.Graph.Nodes)
            {
                if (node is SessionStartNodeDefinition start) return start;
            }
            throw new InvalidOperationException($"Session '{_session.Id}' has no Start node.");
        }

        private GraphNodeDefinition FollowNormal(GraphNodeDefinition node)
        {
            foreach (var output in node.Outputs)
            {
                if (output.Kind == GraphPortKind.Normal)
                {
                    if (!_edgeFromOutput.TryGetValue(output.Id, out var targetId))
                    {
                        throw new InvalidOperationException(
                            $"Session '{_session.Id}': node '{node.Id}' output '{output.Id}' is a dead end with no outgoing edge.");
                    }
                    return _nodesById[targetId];
                }
            }
            throw new InvalidOperationException(
                $"Session '{_session.Id}': node '{node.Id}' has no Normal output to follow.");
        }

        private void WirePhaseEvents(PhaseGraphVm vm)
        {
            vm.PhaseEntered += id => PhaseEntered?.Invoke(id);
            vm.PhaseNodeChanged += id => PhaseNodeChanged?.Invoke(id);
            vm.CardStarted += card => CardStarted?.Invoke(card);
            vm.CardFinished += card => CardFinished?.Invoke(card);
            vm.VariableCheckEvaluated += (check, value, result) => VariableCheckEvaluated?.Invoke(check, value, result);
            vm.RuntimeError += message => RuntimeError?.Invoke(message);
        }
    }

    public enum SessionAdvanceOutcome
    {
        CardExecuted,
        YieldedForCard,
        SessionCompleted,
        Error,
    }

    public sealed class SessionAdvanceResult
    {
        public SessionAdvanceOutcome Outcome { get; }
        public CardDefinition Card { get; }
        public string ErrorMessage { get; }

        private SessionAdvanceResult(SessionAdvanceOutcome outcome, CardDefinition card, string error)
        {
            Outcome = outcome;
            Card = card;
            ErrorMessage = error;
        }

        public static SessionAdvanceResult CardExecuted(CardDefinition card)
        {
            return new SessionAdvanceResult(SessionAdvanceOutcome.CardExecuted, card, null);
        }

        public static readonly SessionAdvanceResult YieldedForCard =
            new SessionAdvanceResult(SessionAdvanceOutcome.YieldedForCard, null, null);

        public static readonly SessionAdvanceResult SessionCompleted =
            new SessionAdvanceResult(SessionAdvanceOutcome.SessionCompleted, null, null);

        public static SessionAdvanceResult Error(string message)
        {
            return new SessionAdvanceResult(SessionAdvanceOutcome.Error, null, message);
        }
    }
}
