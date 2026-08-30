using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// What executing one Action Instance produced. General instances complete
    /// normally (None); flow-control instances declare a transfer request whose
    /// mechanics the graph VM performs (Tickets 08-09) — the executor never
    /// transfers flow itself.
    /// </summary>
    public enum ActionTransfer
    {
        /// <summary>Instance completed; the sequence continues with the next index.</summary>
        None,

        /// <summary>PhaseGoto: transfer to the named exit of the active Phase.</summary>
        PhaseGoto,

        /// <summary>SessionGoto: transfer along this instance's session-graph output.</summary>
        SessionGoto,

        /// <summary>Return: pop the newest saved continuation and resume it.</summary>
        Return,

        /// <summary>EndSession: absolute terminal.</summary>
        EndSession,

        /// <summary>WaitForContinue: yield gameplay until the host continues.</summary>
        WaitForContinue,
    }

    /// <summary>
    /// One ordered point inside a sequence where execution must resume after a
    /// RETURN: the sequence and the next instance index. A transfer inside a
    /// nested PromptChoice option chain captures one point per enclosing
    /// sequence (innermost first).
    /// </summary>
    public sealed class ContinuationPoint
    {
        public ActionSequenceDefinition Sequence { get; }
        public int NextActionIndex { get; }

        public ContinuationPoint(ActionSequenceDefinition sequence, int nextActionIndex)
        {
            Sequence = sequence;
            NextActionIndex = nextActionIndex;
        }
    }

    public sealed class ActionExecutionResult
    {
        public ActionTransfer Transfer { get; set; } = ActionTransfer.None;

        /// <summary>Source action/node id for diagnostics on transfer failures.</summary>
        public string OriginId { get; set; } = "";

        /// <summary>PhaseGoto target (PhaseExit stable id).</summary>
        public string PhaseExitId { get; set; } = "";

        /// <summary>SessionGoto owning Action Instance id — the socket match key at session level.</summary>
        public string SessionGotoInstanceId { get; set; } = "";

        /// <summary>SessionGoto inline label (author-visible; for messages only).</summary>
        public string SessionGotoLabel { get; set; } = "";

        /// <summary>
        /// Card whose Action sequence was interrupted by a transfer. This is
        /// populated by the Phase VM so the Session VM can finish the same Card
        /// after RETURN instead of firing CardFinished early.
        /// </summary>
        public CardDefinition InterruptedCard { get; set; }

        /// <summary>
        /// Continuation points saved at transfer time, innermost sequence first.
        /// A RETURN resumes point 0, then 1, ... before following the graph locus
        /// normal edge. Flow transfers never discard later actions.
        /// </summary>
        public List<ContinuationPoint> Continuation { get; } = new List<ContinuationPoint>();

        public static readonly ActionExecutionResult Continue = new ActionExecutionResult();

        public static ActionExecutionResult PhaseGoto(string exitId)
        {
            return new ActionExecutionResult { Transfer = ActionTransfer.PhaseGoto, PhaseExitId = exitId };
        }

        public static ActionExecutionResult SessionGoto(string instanceId, string label)
        {
            return new ActionExecutionResult
            {
                Transfer = ActionTransfer.SessionGoto,
                SessionGotoInstanceId = instanceId ?? "",
                SessionGotoLabel = label ?? "",
            };
        }

        // These are factories rather than mutable static instances because the
        // sequence runner appends continuation points to transfer results.
        public static ActionExecutionResult ReturnTransfer => new ActionExecutionResult { Transfer = ActionTransfer.Return };

        public static ActionExecutionResult EndSessionTransfer => new ActionExecutionResult { Transfer = ActionTransfer.EndSession };

        public static ActionExecutionResult WaitForContinueTransfer => new ActionExecutionResult { Transfer = ActionTransfer.WaitForContinue };
    }
}
