using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>Base type of every session-graph node.</summary>
    public abstract class SessionGraphNodeDefinition : GraphNodeDefinition
    {
    }

    /// <summary>Singular macro-composition entry of a Session graph; exactly one; owns the Session metadata editing surface.</summary>
    public sealed class SessionStartNodeDefinition : SessionGraphNodeDefinition
    {
    }

    /// <summary>References exactly one reusable Phase by stable ID; its output sockets are projected from that Phase's exported exits.</summary>
    public sealed class PhaseReferenceNodeDefinition : SessionGraphNodeDefinition
    {
        public string PhaseId { get; set; } = "";
    }

    /// <summary>Prompt + up to 3 options (each with an owned sequence); one common normal output plus one unique socket per SessionGoto instance.</summary>
    public sealed class SessionDecisionNodeDefinition : SessionGraphNodeDefinition
    {
        public string Prompt { get; set; } = "";
        public List<SessionDecisionOptionDefinition> Options { get; set; } = new List<SessionDecisionOptionDefinition>();
    }

    /// <summary>One option of a SessionDecision node.</summary>
    public sealed class SessionDecisionOptionDefinition
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public ActionSequenceDefinition Sequence { get; set; } = new ActionSequenceDefinition();
    }

    /// <summary>Absolute terminal; no outputs. Entering it ends the run and discards the entire continuation stack.</summary>
    public sealed class SessionEndNodeDefinition : SessionGraphNodeDefinition
    {
    }
}
