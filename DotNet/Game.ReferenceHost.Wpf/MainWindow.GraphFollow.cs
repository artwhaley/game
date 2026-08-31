using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Nodify;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    public partial class MainWindow
    {
        private ReferencePlayerWindow _referencePlayer;
        private readonly Dictionary<string, GraphEdgeTraversal> _pendingEdgeTraversals =
            new Dictionary<string, GraphEdgeTraversal>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _pendingPhaseNodes =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private void AttachReferencePlayer(ReferencePlayerWindow player)
        {
            _referencePlayer = player;
            player.RunStarted += OnReferenceRunStarted;
            player.SessionNodeChanged += OnReferenceSessionNodeChanged;
            player.PhaseChanged += OnReferencePhaseChanged;
            player.PhaseNodeChanged += OnReferencePhaseNodeChanged;
            player.EdgeTraversed += OnReferenceEdgeTraversed;
            player.CardStarted += OnReferenceCardStarted;
            player.RunStopped += OnReferenceRunStopped;
            player.Closed += OnReferencePlayerClosed;
            player.AutomaticPauseReason = AutomaticPauseReason;
            player.ResumeGuard = ResumeReferenceRun;
        }

        private void DetachReferencePlayer(ReferencePlayerWindow player)
        {
            if (player == null) return;
            player.RunStarted -= OnReferenceRunStarted;
            player.SessionNodeChanged -= OnReferenceSessionNodeChanged;
            player.PhaseChanged -= OnReferencePhaseChanged;
            player.PhaseNodeChanged -= OnReferencePhaseNodeChanged;
            player.EdgeTraversed -= OnReferenceEdgeTraversed;
            player.CardStarted -= OnReferenceCardStarted;
            player.RunStopped -= OnReferenceRunStopped;
            player.Closed -= OnReferencePlayerClosed;
            if (ReferenceEquals(_referencePlayer, player)) _referencePlayer = null;
        }

        private void OnReferencePlayerClosed(object sender, EventArgs e)
        {
            var player = sender as ReferencePlayerWindow;
            DetachReferencePlayer(player);
            _pendingEdgeTraversals.Clear();
            _pendingPhaseNodes.Clear();
            _vm.SessionGraph.ClearTrace();
            _vm.PhaseGraph.ClearTrace();
        }

        private void OnReferenceRunStarted()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                _pendingEdgeTraversals.Clear();
                _pendingPhaseNodes.Clear();
                _vm.SessionGraph.ClearTrace();
                _vm.PhaseGraph.ClearTrace();
            }));
        }

        private void OnReferenceRunStopped()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                _vm.SessionGraph.ClearTrace();
                _vm.PhaseGraph.ClearTrace();
            }));
        }

        private void OnReferenceSessionNodeChanged(string nodeId)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                if (!ReferenceEquals(_referencePlayer, null))
                {
                    _vm.SessionGraph.ApplyNodeTrace(nodeId);
                    BringGraphNodeIntoView(SessionEditor, _vm.SessionGraph, nodeId);
                }
            }));
        }

        private void OnReferencePhaseChanged(string phaseId)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                if (_referencePlayer == null) return;
                if (IsCardBufferDirty)
                {
                    StatusText.Text = "Phase follow waiting for the dirty Card to be saved or reverted.";
                    return;
                }
                if (!_vm.SelectPhaseById(phaseId)) return;
                SyncPhaseListSelection(phaseId);
                ReloadPhaseEditor();
                ApplyPendingReferenceTrace(phaseId);
            }));
        }

        private void OnReferencePhaseNodeChanged(string phaseId, string nodeId)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                if (_referencePlayer == null || string.IsNullOrEmpty(phaseId)) return;
                if (_vm.SelectedPhase?.Id != phaseId)
                {
                    _pendingPhaseNodes[phaseId] = nodeId;
                    return;
                }
                _vm.PhaseGraph.ApplyNodeTrace(nodeId);
                BringGraphNodeIntoView(PhaseEditor, _vm.PhaseGraph, nodeId);
            }));
        }

        private void OnReferenceEdgeTraversed(GraphEdgeTraversal traversal)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                if (_referencePlayer == null || traversal == null) return;
                var graph = traversal.GraphKind == ExecutionGraphKind.Session
                    ? (GraphEditorViewModel)_vm.SessionGraph : _vm.PhaseGraph;
                graph.ApplyTraversal(traversal);
                var matched = graph.Connections.Any(connection =>
                    connection.EdgeId == traversal.EdgeId && connection.GraphOwnerId == traversal.GraphOwnerId);
                if (!matched)
                {
                    _pendingEdgeTraversals[TraceKey(traversal)] = traversal;
                    StatusText.Text = "Live follow: waiting for " + traversal.GraphKind +
                        " edge '" + traversal.EdgeId + "' to load.";
                }
            }));
        }

        private void OnReferenceCardStarted(CardDefinition card)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                if (_referencePlayer == null || card == null || IsCardBufferDirty) return;
                FollowRuntimeCard(card);
            }));
        }

        private string AutomaticPauseReason(ExecutionCheckpoint checkpoint)
        {
            if (checkpoint?.Kind != ExecutionCheckpointKind.BeforeCardActions || !IsCardBufferDirty)
                return null;
            var reason = "Paused — the Workbench has unsaved changes in card “" + DirtyCardTitle +
                "”. Save or Revert that card, then click Resume. The drawn card has not executed.";
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => StatusText.Text = reason));
            return reason;
        }

        private Task<bool> ResumeReferenceRun(ExecutionCheckpoint checkpoint)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                if (IsCardBufferDirty)
                {
                    const string message = "Cannot resume — Save or Revert the dirty Card first.";
                    StatusText.Text = message;
                    completion.TrySetResult(false);
                    return;
                }
                if (checkpoint?.Kind == ExecutionCheckpointKind.BeforeCardActions)
                {
                    var card = _vm.Content?.Cards.FirstOrDefault(item => item.Id == checkpoint.CardId);
                    if (card == null)
                    {
                        StatusText.Text = "Cannot resume — the drawn Card '" + checkpoint.CardId + "' no longer exists.";
                        completion.TrySetResult(false);
                        return;
                    }
                    FollowRuntimeCard(card);
                }
                completion.TrySetResult(true);
            }));
            return completion.Task;
        }

        private void ApplyPendingReferenceTrace(string phaseId)
        {
            if (_pendingPhaseNodes.TryGetValue(phaseId, out var nodeId))
            {
                _pendingPhaseNodes.Remove(phaseId);
                _vm.PhaseGraph.ApplyNodeTrace(nodeId);
                BringGraphNodeIntoView(PhaseEditor, _vm.PhaseGraph, nodeId);
            }
            foreach (var key in _pendingEdgeTraversals.Keys.ToList())
            {
                var traversal = _pendingEdgeTraversals[key];
                if (traversal.GraphKind != ExecutionGraphKind.Phase || traversal.GraphOwnerId != phaseId) continue;
                _pendingEdgeTraversals.Remove(key);
                _vm.PhaseGraph.ApplyTraversal(traversal);
            }
        }

        private static string TraceKey(GraphEdgeTraversal traversal)
            => traversal.GraphKind + ":" + traversal.GraphOwnerId + ":" + traversal.EdgeId;

        private static void BringGraphNodeIntoView(NodifyEditor editor, GraphEditorViewModel graph, string nodeId)
        {
            if (editor == null || graph == null || string.IsNullOrEmpty(nodeId)) return;
            var node = graph.Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node == null) return;
            editor.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                // Nodify's BringIntoView overload pans its own graph viewport;
                // it does not scroll the surrounding Workbench panes.
                editor.BringIntoView(new Rect(node.Location, new Size(220, 140)), 48);
            }));
        }
    }
}
