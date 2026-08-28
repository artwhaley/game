using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// One authored Session: metadata plus exactly one macro-composition graph over
    /// reusable Phases. There is no PhaseSlot and no arbitrary slot-count limit —
    /// progression is whatever the authored graph wires.
    ///
    /// Session free-form tags are gone with the old tag system; classification
    /// is the SessionType reference. Card preference weighting is owned here.
    /// </summary>
    public sealed class SessionDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Exactly one referenced SessionType by stable ID.</summary>
        public string SessionTypeId { get; set; } = "";

        /// <summary>Kink/Happiness preference weighting for Card selection (all default 1.0, nonnegative).</summary>
        public SessionCardWeightingDefinition CardWeighting { get; set; } = new SessionCardWeightingDefinition();

        public SessionGraphDefinition Graph { get; set; } = new SessionGraphDefinition();
    }

    /// <summary>
    /// Per-Session Card preference weights. See Docs/MilestoneB/01-contract.md:
    /// Love/Like score rises with Happiness, Torture score rises as Happiness
    /// falls; every value nonnegative, defaults 1.0.
    /// </summary>
    public sealed class SessionCardWeightingDefinition
    {
        public float LoveBase { get; set; } = 1f;
        public float LoveHappinessGain { get; set; } = 1f;
        public float LikeBase { get; set; } = 1f;
        public float LikeHappinessGain { get; set; } = 1f;
        public float TortureBase { get; set; } = 1f;
        public float TortureUnhappinessGain { get; set; } = 1f;
    }

    /// <summary>The macro composition layer: SessionStart → exact Phase references → SessionEnd, with decisions/gotos in between.</summary>
    public sealed class SessionGraphDefinition
    {
        public List<SessionGraphNodeDefinition> Nodes { get; set; } = new List<SessionGraphNodeDefinition>();

        /// <summary>Directed edges between owned outputs and target nodes of this graph.</summary>
        public List<GraphEdgeDefinition> Edges { get; set; } = new List<GraphEdgeDefinition>();
    }
}
