using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// Which connector a graph output represents.
    ///
    /// Validity is per graph level (enforced by repositories/loader, and fail-loud
    /// at runtime):
    /// - Phase graph ports: Normal, True, False.
    /// - Session graph ports: Normal (SessionStart / SessionDecision common output),
    ///   PhaseExit (one projected port per referenced Phase's exported exit),
    ///   SessionGoto (one unique port per SessionGoto Action Instance).
    /// </summary>
    public enum GraphPortKind
    {
        Normal = 0,
        True = 1,
        False = 2,
        PhaseExit = 3,
        SessionGoto = 4,
    }

    /// <summary>
    /// A named source output ("socket") owned by one graph node. Every output has a
    /// stable unique ID within its snapshot; at most one outgoing edge may leave an
    /// output (enforced at persistence and honored by graph-walking code).
    /// </summary>
    public sealed class GraphOutputDefinition
    {
        public string Id { get; set; } = "";

        public GraphPortKind Kind { get; set; } = GraphPortKind.Normal;

        /// <summary>
        /// For <see cref="GraphPortKind.PhaseExit"/>: the stable ID of the referenced
        /// Phase's exported exit this socket projects. Edge wiring survives exit renames
        /// because identity is the ID.
        /// </summary>
        public string PhaseExitId { get; set; } = "";

        /// <summary>
        /// For <see cref="GraphPortKind.SessionGoto"/>: the ID of the owning
        /// SessionGoto Action Instance. Deleting the instance deletes its port/edge
        /// as one authored unit.
        /// </summary>
        public string SessionGotoActionInstanceId { get; set; } = "";

        /// <summary>
        /// Optional display label persisted with the socket (session_goto sockets
        /// render their instance's label here). PhaseExit sockets derive their shown
        /// name live from the referenced exit and leave this empty.
        /// </summary>
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// A directed edge between an output socket on some node and a target node in the
    /// same graph. Multiple incoming edges to one node are legal.
    /// </summary>
    public sealed class GraphEdgeDefinition
    {
        public string Id { get; set; } = "";

        /// <summary>ID of the <see cref="GraphOutputDefinition"/> this edge leaves from.</summary>
        public string SourceOutputId { get; set; } = "";

        /// <summary>ID of the target node this edge enters.</summary>
        public string TargetNodeId { get; set; } = "";
    }

    /// <summary>
    /// Common identity/output surface shared by all session- and phase-level graph
    /// node definitions.
    /// </summary>
    public abstract class GraphNodeDefinition
    {
        public string Id { get; set; } = "";

        /// <summary>This node's owned output sockets. Order is significant for UI display; identity is the ID.</summary>
        public List<GraphOutputDefinition> Outputs { get; set; } = new List<GraphOutputDefinition>();
    }
}
