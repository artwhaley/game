using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// One reusable Phase: metadata plus exactly one low-level executable graph plus
    /// its exported exits. A Phase has no implicit completion — it runs until its
    /// authored graph executes a Phase GOTO, RETURN, or EndSession; a dead-end
    /// without a control transfer is a runtime graph error.
    ///
    /// Phase classification tags are gone (Sessions hand-author an exact Phase
    /// set). Card selection is the include-only Card tag query below; NOT-style
    /// exclusion is deliberately deferred to a future status-effect system.
    /// </summary>
    public sealed class PhaseDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Card must carry ALL of these CardTags to be eligible (empty = no restriction).</summary>
        public List<string> MustHaveAllCardTags { get; set; } = new List<string>();

        /// <summary>Card must carry ANY of these CardTags to be eligible (empty = no restriction).</summary>
        public List<string> MustHaveAnyCardTags { get; set; } = new List<string>();

        /// <summary>Ordered exported exits; identity is the exit ID, display name is editable freely.</summary>
        public List<PhaseExitDefinition> Exits { get; set; } = new List<PhaseExitDefinition>();

        public PhaseGraphDefinition Graph { get; set; } = new PhaseGraphDefinition();
    }

    /// <summary>The low-level executable layer of one Phase.</summary>
    public sealed class PhaseGraphDefinition
    {
        public List<GraphNodeDefinition> Nodes { get; set; } = new List<GraphNodeDefinition>();

        /// <summary>Directed edges between owned outputs and target nodes of this graph.</summary>
        public List<GraphEdgeDefinition> Edges { get; set; } = new List<GraphEdgeDefinition>();
    }
}
