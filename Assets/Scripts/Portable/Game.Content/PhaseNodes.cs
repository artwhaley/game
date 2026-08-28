using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>How a VariableCheck obtains the live numeric value it compares.</summary>
    public enum VariableSourceKind
    {
        PhaseProgress = 0,
        Temperature = 1,
        Stat = 2,
    }

    /// <summary>Comparison operators available to VariableCheck v1 (single literal value on the right side).</summary>
    public enum VariableCompareOperator
    {
        LessThan = 0,
        LessThanOrEqual = 1,
        Equal = 2,
        NotEqual = 3,
        GreaterThanOrEqual = 4,
        GreaterThan = 5,
    }

    /// <summary>Base type of every phase-graph node. Keeping the low-level layer strongly typed means a session-level node can never appear in a phase graph or vice versa.</summary>
    public abstract class PhaseGraphNodeDefinition : GraphNodeDefinition
    {
    }

    /// <summary>Singular entry node of a Phase's low-level graph. Exactly one; cannot be deleted; selecting it edits Phase-wide metadata.</summary>
    public sealed class PhaseEntryNodeDefinition : PhaseGraphNodeDefinition
    {
    }

    /// <summary>Card-selection node: executes eligible Cards until an authored yield or transfer. No eligible Card is a clear runtime error in v1.</summary>
    public sealed class CardExecutorNodeDefinition : PhaseGraphNodeDefinition
    {
    }

    /// <summary>Evaluates one live numeric value against one literal and follows its True or False output. Compose complex logic by wiring checks, not expressions.</summary>
    public sealed class VariableCheckNodeDefinition : PhaseGraphNodeDefinition
    {
        public VariableSourceKind SourceKind { get; set; } = VariableSourceKind.PhaseProgress;

        /// <summary>Name for Temperature/Stat sources (stable ID / stat key). Unused for PhaseProgress.</summary>
        public string VariableKey { get; set; } = "";

        public VariableCompareOperator Operator { get; set; } = VariableCompareOperator.LessThan;
        public float CompareValue { get; set; }
    }

    /// <summary>Ordered Action Instance sequence with one normal output.</summary>
    public sealed class ActionNodeDefinition : PhaseGraphNodeDefinition
    {
        public ActionSequenceDefinition Sequence { get; set; } = new ActionSequenceDefinition();
    }

    /// <summary>Prompt + up to 3 options, each owning an ordered sequence; after a normal option completion flow continues through the common normal output.</summary>
    public sealed class PhaseDecisionNodeDefinition : PhaseGraphNodeDefinition
    {
        public string Prompt { get; set; } = "";
        public List<PhaseDecisionOptionDefinition> Options { get; set; } = new List<PhaseDecisionOptionDefinition>();
    }

    /// <summary>One option of a PhaseDecision node.</summary>
    public sealed class PhaseDecisionOptionDefinition
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public ActionSequenceDefinition Sequence { get; set; } = new ActionSequenceDefinition();
    }

    /// <summary>Terminal convenience node: executing it performs RETURN semantics against the continuation stack.</summary>
    public sealed class ReturnNodeDefinition : PhaseGraphNodeDefinition
    {
    }
}
