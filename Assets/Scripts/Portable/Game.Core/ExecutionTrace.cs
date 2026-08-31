using System;
using System.Threading;
using System.Threading.Tasks;

namespace TruthCardGame.Core
{
    /// <summary>Identifies the graph level represented by an execution event.</summary>
    public enum ExecutionGraphKind
    {
        Session = 0,
        Phase = 1,
    }

    /// <summary>One exact logical graph edge selected by the portable VM.</summary>
    public sealed class GraphEdgeTraversal
    {
        public GraphEdgeTraversal(ExecutionGraphKind graphKind, string graphOwnerId,
            string edgeId, string sourceOutputId, string targetNodeId)
        {
            GraphKind = graphKind;
            GraphOwnerId = graphOwnerId ?? "";
            EdgeId = edgeId ?? "";
            SourceOutputId = sourceOutputId ?? "";
            TargetNodeId = targetNodeId ?? "";
        }

        public ExecutionGraphKind GraphKind { get; }
        public string GraphOwnerId { get; }
        public string EdgeId { get; }
        public string SourceOutputId { get; }
        public string TargetNodeId { get; }
    }

    public enum ExecutionCheckpointKind
    {
        BeforeSessionNode = 0,
        BeforePhaseNode = 1,
        BeforeCardActions = 2,
    }

    /// <summary>A safe, host-observable point at which execution may cooperatively wait.</summary>
    public sealed class ExecutionCheckpoint
    {
        public ExecutionCheckpoint(ExecutionCheckpointKind kind, ExecutionGraphKind graphKind,
            string graphOwnerId, string nodeId, string cardId = null)
        {
            Kind = kind;
            GraphKind = graphKind;
            GraphOwnerId = graphOwnerId ?? "";
            NodeId = nodeId ?? "";
            CardId = cardId;
        }

        public ExecutionCheckpointKind Kind { get; }
        public ExecutionGraphKind GraphKind { get; }
        public string GraphOwnerId { get; }
        public string NodeId { get; }
        public string CardId { get; }
    }

    /// <summary>Optional host gate. The default implementation never waits.</summary>
    public interface IExecutionPauseGate
    {
        Task WaitAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken);
    }

    public sealed class NoOpExecutionPauseGate : IExecutionPauseGate
    {
        public static readonly NoOpExecutionPauseGate Instance = new NoOpExecutionPauseGate();

        private NoOpExecutionPauseGate() { }

        public Task WaitAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
