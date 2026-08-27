using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// One authored Session: metadata plus exactly one macro-composition graph over
    /// reusable Phases. There is no PhaseSlot and no arbitrary slot-count limit —
    /// progression is whatever the authored graph wires.
    /// </summary>
    public sealed class SessionDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Free-form authored tags retained on the Session itself.</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>Exactly one referenced SessionType by stable ID.</summary>
        public string SessionTypeId { get; set; } = "";

        public SessionGraphDefinition Graph { get; set; } = new SessionGraphDefinition();
    }

    /// <summary>The macro composition layer: SessionStart → exact Phase references → SessionEnd, with decisions/gotos in between.</summary>
    public sealed class SessionGraphDefinition
    {
        public List<SessionGraphNodeDefinition> Nodes { get; set; } = new List<SessionGraphNodeDefinition>();

        /// <summary>Directed edges between owned outputs and target nodes of this graph.</summary>
        public List<GraphEdgeDefinition> Edges { get; set; } = new List<GraphEdgeDefinition>();
    }
}
