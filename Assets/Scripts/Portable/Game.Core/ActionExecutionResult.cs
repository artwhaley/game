namespace TruthCardGame.Core
{
    /// <summary>
    /// What executing one Action Instance produced. General instances complete
    /// normally (Continue); flow-control instances declare a transfer request
    /// whose mechanics the graph VM performs (Tickets 08-09) — the executor
    /// never transfers flow itself.
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
    }

    public sealed class ActionExecutionResult
    {
        public ActionTransfer Transfer { get; set; } = ActionTransfer.None;

        /// <summary>PhaseGoto target (PhaseExit stable id).</summary>
        public string PhaseExitId { get; set; } = "";

        /// <summary>SessionGoto inline label (author-visible; socket matched at session level).</summary>
        public string SessionGotoLabel { get; set; } = "";

        public static readonly ActionExecutionResult Continue = new ActionExecutionResult();

        public static ActionExecutionResult PhaseGoto(string exitId)
        {
            return new ActionExecutionResult { Transfer = ActionTransfer.PhaseGoto, PhaseExitId = exitId };
        }

        public static ActionExecutionResult SessionGoto(string label)
        {
            return new ActionExecutionResult { Transfer = ActionTransfer.SessionGoto, SessionGotoLabel = label };
        }

        public static readonly ActionExecutionResult ReturnTransfer = new ActionExecutionResult { Transfer = ActionTransfer.Return };

        public static readonly ActionExecutionResult EndSessionTransfer = new ActionExecutionResult { Transfer = ActionTransfer.EndSession };
    }
}
