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
    /// EndSession clears the entire stack. PhaseGoto and SessionGoto both land
    /// in the same session-node enter path.
    /// </summary>
    public sealed class SessionGraphVm
    {
        private readonly GameContentDefinition _content;
        private readonly ContentCatalog _catalog;
        private readonly CoreServices _services;
        private readonly BackgroundActionTracker _tracker;
        private readonly PhaseRunRngFactory _rngFactory;
        private readonly CardSelectionProfile _selectionProfile;
        private readonly SessionDefinition _session;
        private readonly ActionExecutor _executor;
        private readonly int _executionBudget;

        private readonly Dictionary<string, GraphNodeDefinition> _nodesById;
        private readonly Dictionary<string, GraphOutputDefinition> _outputsById;
        private readonly Dictionary<string, GraphEdgeDefinition> _edgeFromOutput;

        private readonly ContinuationStack _stack = new ContinuationStack();

        private GraphNodeDefinition _sessionNode;
        private PhaseRun _activeRun;
        private PhaseGraphVm _activePhaseVm;

        /// <summary>Set by RETURN: the next loop iteration resumes this saved frame instead of advancing fresh.</summary>
        private ContinuationFrame _pendingResume;

        // SessionDecision option sequences can also contain an explicit wait.
        // This is separate from the GOTO/RETURN stack: WaitForContinue is not a
        // call frame and resumes in place on the next RunUntilYield call.
        private GraphNodeDefinition _suspendedSessionLocus;
        private IReadOnlyList<ContinuationPoint> _suspendedSessionChain;

        public event Action<string> SessionNodeChanged;      // node id
        public event Action<string> PhaseEntered;            // phase id (forwarded)
        public event Action<string> PhaseNodeChanged;        // node id (forwarded)
        public event Action<CardDefinition> CardStarted;
        public event Action<CardDefinition> CardFinished;
        public event Action<VariableCheckNodeDefinition, float, bool> VariableCheckEvaluated;
        public event Action<string> RuntimeError;
        public event Action<GraphEdgeTraversal> EdgeTraversed;
        public event Action SessionCompleted;

        /// <summary>Forwarded from active PhaseGraphVms (selection diagnostics seam).</summary>
        public event Action<CardSelector.SelectionResult> CardSelectionEvaluated;

        public SessionGraphVm(
            GameContentDefinition content, string sessionId, CoreServices services,
            BackgroundActionTracker tracker, PhaseRunRngFactory rngFactory,
            int executionBudget = PhaseGraphVm.DefaultExecutionBudget,
            CardSelectionProfile selectionProfile = null)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _catalog = new ContentCatalog(content);
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _rngFactory = rngFactory ?? new PhaseRunRngFactory();
            _selectionProfile = selectionProfile ?? new CardSelectionProfile();
            if (executionBudget <= 0) throw new ArgumentOutOfRangeException(nameof(executionBudget));
            _executionBudget = executionBudget;
            _session = _catalog.SessionById(sessionId);
            _executor = new ActionExecutor(_tracker);

            if (_session.Graph == null) throw new InvalidOperationException($"Session '{sessionId}' has no graph.");

            _nodesById = new Dictionary<string, GraphNodeDefinition>();
            _outputsById = new Dictionary<string, GraphOutputDefinition>();
            _edgeFromOutput = new Dictionary<string, GraphEdgeDefinition>();

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
                _edgeFromOutput[edge.SourceOutputId] = edge;
            }
        }

        public bool IsComplete { get; private set; }

        /// <summary>Current session graph node; null until the first advance.</summary>
        public string CurrentSessionNodeId => _sessionNode?.Id;

        public int ContinuationDepth => _stack.Count;

        /// <summary>Phase id of the active phase run (null before the first phase enters).</summary>
        public string CurrentPhaseId => _activeRun?.PhaseId;

        /// <summary>Phase-local progress of the active run (0 before the first phase enters).</summary>
        public float CurrentProgress => _activeRun?.Progress.Value ?? 0f;

        /// <summary>Debugger view (Ticket 19): the active phase run's placement node id.</summary>
        public string CurrentPlacementNodeId => _activeRun?.PlacementNodeId;

        /// <summary>Debugger view (Ticket 19): readable continuation frames, outermost first.</summary>
        public IReadOnlyList<(string PhaseId, string NodeId)> ContinuationSummary() => _stack.DebugSummary();

        /// <summary>
        /// Runs the whole session graph until an authored yield, end, or error.
        /// Card completion alone is not a pause boundary.
        /// </summary>
        public Task<SessionAdvanceResult> AdvanceAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            return RunUntilYieldAsync(context, cancellationToken);
        }

        /// <summary>Runs the whole Session graph until an authored wait, end, or error.</summary>
        public async Task<SessionAdvanceResult> RunUntilYieldAsync(ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            if (IsComplete)
            {
                return SessionAdvanceResult.SessionCompleted;
            }

            try
            {
                return await AdvanceCoreAsync(context, cancellationToken, new GraphExecutionBudget(_executionBudget));
            }
            catch (InvalidOperationException ex)
            {
                RuntimeError?.Invoke(ex.Message);
                return SessionAdvanceResult.Error(ex.Message);
            }
        }

        private async Task<SessionAdvanceResult> AdvanceCoreAsync(ActionExecutionContext context,
            CancellationToken cancellationToken, GraphExecutionBudget budget)
        {
            while (!IsComplete)
            {
                budget.Consume("session node", sessionId: _session.Id, nodeId: _sessionNode?.Id);

                cancellationToken.ThrowIfCancellationRequested();

                if (_suspendedSessionLocus != null)
                {
                    var locus = _suspendedSessionLocus;
                    var chain = _suspendedSessionChain;
                    _suspendedSessionLocus = null;
                    _suspendedSessionChain = null;
                    var sessionContext = SessionDecisionContext(context);
                    var resumed = await _executor.ResumeChainAsync(chain ?? Array.Empty<ContinuationPoint>(),
                        sessionContext, cancellationToken, budget);
                    if (resumed.Transfer == ActionTransfer.WaitForContinue)
                    {
                        _suspendedSessionLocus = locus;
                        _suspendedSessionChain = resumed.Continuation;
                        return SessionAdvanceResult.YieldedForContinue;
                    }
                    if (resumed.Transfer != ActionTransfer.None)
                    {
                        _sessionNode = locus;
                        await HandleTransferAsync(resumed, context, cancellationToken);
                    }
                    else
                    {
                        _sessionNode = FollowNormal(locus);
                        SessionNodeChanged?.Invoke(_sessionNode.Id);
                    }
                }
                else if (_pendingResume != null)
                {
                    var frame = _pendingResume;
                    _pendingResume = null;
                    var resumed = await ResumeFrameAsync(frame, context, cancellationToken, budget);
                    if (resumed != null) return resumed;
                }
                else if (_activeRun != null)
                {
                    var phaseResult = await _activePhaseVm.RunUntilYieldAsync(_activeRun, context, cancellationToken, budget);
                    var result = await HandlePhaseResultAsync(phaseResult, context, cancellationToken);
                    if (result != null) return result;
                }
                else
                {
                    await StepSessionNodeAsync(context, cancellationToken, budget);
                    if (_suspendedSessionLocus != null)
                    {
                        return SessionAdvanceResult.YieldedForContinue;
                    }
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
                case PhaseAdvanceOutcome.YieldedForContinue:
                    return SessionAdvanceResult.YieldedForContinue;

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

        private async Task StepSessionNodeAsync(ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget)
        {
            if (_sessionNode == null)
            {
                _sessionNode = FindStart();
                SessionNodeChanged?.Invoke(_sessionNode.Id);
            }

            await context.Services.PauseGate.WaitAsync(
                new ExecutionCheckpoint(ExecutionCheckpointKind.BeforeSessionNode,
                    ExecutionGraphKind.Session, _session.Id, _sessionNode.Id), cancellationToken);

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
                    await EnterDecisionAsync(decision, context, cancellationToken, budget);
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
            _activePhaseVm = CreatePhaseVm(_catalog.PhaseById(reference.PhaseId));
            WirePhaseEvents(_activePhaseVm);
            return Task.CompletedTask;
        }

        private async Task EnterDecisionAsync(SessionDecisionNodeDefinition decision, ActionExecutionContext context,
            CancellationToken cancellationToken, GraphExecutionBudget budget)
        {
            if (decision.Options.Count > 3)
            {
                throw new InvalidOperationException(
                    $"SessionDecision '{decision.Id}' has {decision.Options.Count} options; " +
                    "the authoring contract allows at most 3.");
            }

            var selected = await PromptForSessionDecisionAsync(decision, context, cancellationToken);
            if (selected != null && selected.Sequence != null && selected.Sequence.Instances.Count > 0)
            {
                var optionContext = SessionDecisionContext(context);
                var optionResult = await _executor.ExecuteSequenceAsync(selected.Sequence, optionContext, cancellationToken, budget);
                if (optionResult.Transfer == ActionTransfer.WaitForContinue)
                {
                    _sessionNode = decision;
                    _suspendedSessionLocus = decision;
                    _suspendedSessionChain = optionResult.Continuation;
                    return;
                }
                if (optionResult.Transfer != ActionTransfer.None)
                {
                    await HandleTransferAsync(optionResult, context, cancellationToken);
                    return;
                }
            }

            _sessionNode = FollowNormal(decision);
            SessionNodeChanged?.Invoke(_sessionNode.Id);
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
                    await HandleReturnAsync(transfer, context, cancellationToken);
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
            _stack.Push(new ContinuationFrame(_activeRun.CurrentNode, _activeRun, transfer.Continuation,
                transfer.InterruptedCard));

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

            if (!_edgeFromOutput.TryGetValue(socketId, out var edge))
            {
                throw new InvalidOperationException(
                    $"Unwired phase exit: projected socket '{socketId}' (exit '{transfer.PhaseExitId}') of Session '{_session.Id}' has no outgoing edge.");
            }

            // Transfer: the phase run is now suspended on the stack.
            _activeRun = null;
            _activePhaseVm = null;
            _sessionNode = _nodesById[edge.TargetNodeId];
            EdgeTraversed?.Invoke(new GraphEdgeTraversal(
                ExecutionGraphKind.Session, _session.Id, edge.Id, edge.SourceOutputId, edge.TargetNodeId));
            SessionNodeChanged?.Invoke(_sessionNode.Id);
        }

        /// <summary>
        /// SessionGoto: valid only inside a SessionDecision option sequence. Push
        /// the continuation (locus = the decision node, no PhaseRun), resolve the
        /// instance's own unique session output socket, and transfer to whatever
        /// session node that edge targets. If the target returns later, the
        /// option sequence resumes at the next action, then the decision's common
        /// normal edge is followed.
        /// </summary>
        private async Task ResolveSessionGotoAsync(ActionExecutionResult transfer, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (!(_sessionNode is SessionDecisionNodeDefinition decision))
            {
                throw new InvalidOperationException(
                    $"SessionGoto '{transfer.SessionGotoLabel}' fired with no active SessionDecision node.");
            }

            // Save the continuation BEFORE transferring: session-level frame.
            _stack.Push(new ContinuationFrame(decision, null, transfer.Continuation, null));

            var socket = FindSessionGotoSocket(decision, transfer.SessionGotoInstanceId);
            if (socket == null)
            {
                throw new InvalidOperationException(
                    $"SessionGoto '{transfer.SessionGotoLabel}' has no unique output socket " +
                    $"on decision '{decision.Id}'.");
            }

            if (!_edgeFromOutput.TryGetValue(socket.Id, out var edge))
            {
                throw new InvalidOperationException(
                    $"Unwired SessionGoto: socket '{socket.Id}' ('{transfer.SessionGotoLabel}') " +
                    $"of decision '{decision.Id}' has no outgoing edge.");
            }

            _sessionNode = _nodesById[edge.TargetNodeId];
            EdgeTraversed?.Invoke(new GraphEdgeTraversal(
                ExecutionGraphKind.Session, _session.Id, edge.Id, edge.SourceOutputId, edge.TargetNodeId));
            SessionNodeChanged?.Invoke(_sessionNode.Id);
        }

        /// <summary>
        /// Resumes a saved frame after RETURN. Session-level frames (PhaseRun
        /// null, locus a SessionDecision) resume the option chain in session
        /// scope, then follow the decision's common normal edge. Phase-level
        /// frames resume through the phase VM as before.
        /// </summary>
        private async Task<SessionAdvanceResult> ResumeFrameAsync(
            ContinuationFrame frame, ActionExecutionContext context, CancellationToken cancellationToken,
            GraphExecutionBudget budget)
        {
            if (frame.PhaseRun == null)
            {
                _sessionNode = frame.GraphLocus;
                if (frame.Chain != null && frame.Chain.Count > 0)
                {
                    var optionContext = SessionDecisionContext(context);
                    var chainResult = await _executor.ResumeChainAsync(frame.Chain, optionContext, cancellationToken, budget);
                    if (chainResult.Transfer == ActionTransfer.WaitForContinue)
                    {
                        _suspendedSessionLocus = frame.GraphLocus;
                        _suspendedSessionChain = chainResult.Continuation;
                        return SessionAdvanceResult.YieldedForContinue;
                    }
                    if (chainResult.Transfer != ActionTransfer.None)
                    {
                        await HandleTransferAsync(chainResult, context, cancellationToken);
                        return null;
                    }
                }

                if (frame.GraphLocus is SessionDecisionNodeDefinition decision)
                {
                    _sessionNode = FollowNormal(decision);
                    SessionNodeChanged?.Invoke(_sessionNode.Id);
                }
                return null; // keep the session loop going
            }

            var resumeResult = await _activePhaseVm.ResumeFromContinuationAsync(
                _activeRun, frame.GraphLocus, frame.Chain, context, cancellationToken, budget, frame.Card);
            return await HandlePhaseResultAsync(resumeResult, context, cancellationToken);
        }

        /// <summary>
        /// Return: pop the newest continuation; restore the exact frame (locus,
        /// PhaseRun with progress/RNG/history, chain). Temperatures stay current
        /// because they live in the shared context, untouched by transfers.
        /// The session loop resumes the frame via ResumeFromContinuationAsync on
        /// the next iteration.
        ///
        /// A RETURN with an empty continuation stack is malformed content. The
        /// author must wire an explicit PhaseGoto to a PhaseExit or use
        /// SessionEnd for a terminal path; RETURN never invents a fallback.
        /// </summary>
        private Task HandleReturnAsync(ActionExecutionResult transfer, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (_stack.IsEmpty)
            {
                var phaseId = _activeRun?.PhaseId ?? "<none>";
                var nodeId = _activeRun?.CurrentNode?.Id ?? "<none>";
                var origin = string.IsNullOrEmpty(transfer?.OriginId) ? nodeId : transfer.OriginId;
                throw new InvalidOperationException(
                    $"Session '{_session.Id}' Phase '{phaseId}' RETURN '{origin}' completed with an empty continuation stack. " +
                    "Wire an explicit PhaseGoto to a PhaseExit or use SessionEnd for a terminal path.");
            }

            var frame = _stack.Pop();

            if (frame.PhaseRun == null)
            {
                // Session-level frame (SessionDecision option sequence).
                _activeRun = null;
                _activePhaseVm = null;
            }
            else
            {
                _activeRun = frame.PhaseRun;
                _activePhaseVm = CreatePhaseVm(_catalog.PhaseById(frame.PhaseRun.PhaseId));
                WirePhaseEvents(_activePhaseVm);
            }
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

        private GraphOutputDefinition FindSessionGotoSocket(SessionDecisionNodeDefinition decision, string instanceId)
        {
            foreach (var output in decision.Outputs)
            {
                if (output.Kind == GraphPortKind.SessionGoto && output.SessionGotoActionInstanceId == instanceId)
                {
                    return output;
                }
            }
            return null;
        }

        private static ActionExecutionContext SessionDecisionContext(ActionExecutionContext baseContext)
        {
            return new ActionExecutionContext(
                baseContext.Player,
                baseContext.Services,
                baseContext.Catalog,
                baseContext.Temperatures,
                null, // no PhaseRun at session level
                ActionOwnerScope.SessionDecisionOptionSequence,
                baseContext.DialogRng);
        }

        private async Task<SessionDecisionOptionDefinition> PromptForSessionDecisionAsync(
            SessionDecisionNodeDefinition decision, ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (context.Services.Prompts == null)
            {
                _services.Log.Warning(
                    $"SessionDecision '{decision.Id}' has no prompt service; continuing without a choice.");
                return null;
            }

            var labels = new List<string>();
            foreach (var option in decision.Options) labels.Add(option.Label);

            var selectedIndex = await context.Services.Prompts.AskAsync(decision.Prompt, labels, cancellationToken);
            if (selectedIndex == null || selectedIndex < 0 || selectedIndex >= decision.Options.Count)
            {
                return null;
            }
            return decision.Options[selectedIndex.Value];
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
                    if (!_edgeFromOutput.TryGetValue(output.Id, out var edge))
                    {
                        throw new InvalidOperationException(
                            $"Session '{_session.Id}': node '{node.Id}' output '{output.Id}' is a dead end with no outgoing edge.");
                    }
                    var target = _nodesById[edge.TargetNodeId];
                    EdgeTraversed?.Invoke(new GraphEdgeTraversal(
                        ExecutionGraphKind.Session, _session.Id, edge.Id,
                        edge.SourceOutputId, edge.TargetNodeId));
                    return target;
                }
            }
            throw new InvalidOperationException(
                $"Session '{_session.Id}': node '{node.Id}' has no Normal output to follow.");
        }

        private PhaseGraphVm CreatePhaseVm(PhaseDefinition phase)
        {
            return new PhaseGraphVm(_content, phase, _services, _tracker, _executionBudget,
                _selectionProfile, _session.CardWeighting, _session.Id);
        }

        private void WirePhaseEvents(PhaseGraphVm vm)
        {
            vm.PhaseEntered += id => PhaseEntered?.Invoke(id);
            vm.PhaseNodeChanged += id => PhaseNodeChanged?.Invoke(id);
            vm.EdgeTraversed += edge => EdgeTraversed?.Invoke(edge);
            vm.CardStarted += card => CardStarted?.Invoke(card);
            vm.CardFinished += card => CardFinished?.Invoke(card);
            vm.VariableCheckEvaluated += (check, value, result) => VariableCheckEvaluated?.Invoke(check, value, result);
            vm.RuntimeError += message => RuntimeError?.Invoke(message);
            vm.CardSelectionEvaluated += result => CardSelectionEvaluated?.Invoke(result);
        }
    }

    public enum SessionAdvanceOutcome
    {
        /// <summary>An authored WaitForContinue paused the graph.</summary>
        YieldedForContinue,
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

        public static readonly SessionAdvanceResult YieldedForContinue =
            new SessionAdvanceResult(SessionAdvanceOutcome.YieldedForContinue, null, null);

        public static readonly SessionAdvanceResult SessionCompleted =
            new SessionAdvanceResult(SessionAdvanceOutcome.SessionCompleted, null, null);

        public static SessionAdvanceResult Error(string message)
        {
            return new SessionAdvanceResult(SessionAdvanceOutcome.Error, null, message);
        }
    }
}
