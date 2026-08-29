using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// One authored occurrence of an Action inside exactly one owned sequence.
    /// Instances are never shared between sequences or cards; editing one never
    /// affects another instance of the same Action Type. Every instance carries a
    /// stable unique occurrence ID and instance-local parameter values.
    ///
    /// There is no generic parameter bag (no EAV): each Action Type is an explicit
    /// subclass here, backed by an explicit SQLite subtype table, a registry entry,
    /// and an explicit WPF editor. Adding a new Action Type is the documented
    /// vertical slice in Docs/GraphWorkbench/01-architecture-decisions.md §16.
    /// </summary>
    public abstract class ActionInstanceDefinition
    {
        public string Id { get; set; } = "";

        /// <summary>
        /// Whether this occurrence blocks graph continuation until it completes.
        /// Flow-control Action Types (PhaseGoto, SessionGoto, Return, EndSession)
        /// are always control-synchronous regardless of this flag (registry-guaranteed).
        /// </summary>
        public bool IsBlocking { get; set; } = true;
    }

    /// <summary>Log/debug message via the host's log service. Legal everywhere.</summary>
    public sealed class DebugInstanceDefinition : ActionInstanceDefinition
    {
        public string Message { get; set; } = "";
        public float DelaySeconds { get; set; }
    }

    /// <summary>Adjusts a named runtime stat by a fixed amount.</summary>
    public sealed class StatIncreaseInstanceDefinition : ActionInstanceDefinition
    {
        public string StatKey { get; set; } = "";
        public float Amount { get; set; }
    }

    /// <summary>Adjusts PhaseProgress of the active PhaseRun. Not clamped: zero/negative mutation is legal. There is no automatic post-card increment anywhere else.</summary>
    public sealed class IncrementProgressInstanceDefinition : ActionInstanceDefinition
    {
        public float Amount { get; set; } = 10f;
    }

    /// <summary>Adjusts one Session-global Temperature (resolved by stable ID); mutation clamps to the definition bounds.</summary>
    public sealed class ModifyTemperatureInstanceDefinition : ActionInstanceDefinition
    {
        public string TemperatureId { get; set; } = "";
        public float Amount { get; set; }
    }

    /// <summary>Asks the host to play a cutscene bound to a Resource. Empty ResourceId is legal and means "unmapped placeholder" (host logs it).</summary>
    public sealed class CutsceneInstanceDefinition : ActionInstanceDefinition
    {
        public string ResourceId { get; set; } = "";
    }

    /// <summary>Displays authored dialog through the temporary cutscene-style host fixture.</summary>
    public sealed class DialogInstanceDefinition : ActionInstanceDefinition
    {
        public string Text { get; set; } = "";
    }

    /// <summary>Waits for an authored number of seconds through the host clock.</summary>
    public sealed class DelayInstanceDefinition : ActionInstanceDefinition
    {
        public float DurationSeconds { get; set; }
    }

    /// <summary>Runs one configured smart-toy capability at an authored intensity and duration.</summary>
    public sealed class ToyActivityInstanceDefinition : ActionInstanceDefinition
    {
        public string CapabilityId { get; set; } = "";
        public float Intensity { get; set; }
        public float DurationSeconds { get; set; }
    }

    /// <summary>Prompts the user with labeled options; the selected option executes its own full nested Action sequence before normal completion.</summary>
    public sealed class PromptChoiceInstanceDefinition : ActionInstanceDefinition
    {
        public string Prompt { get; set; } = "";
        public List<PromptChoiceOptionDefinition> Options { get; set; } = new List<PromptChoiceOptionDefinition>();
    }

    /// <summary>
    /// Explicit gameplay yield. The current run suspends immediately after this
    /// instance and resumes at the following instance when the host continues.
    /// It carries no v1 parameters and never creates a GOTO continuation frame.
    /// </summary>
    public sealed class WaitForContinueInstanceDefinition : ActionInstanceDefinition
    {
    }

    /// <summary>One option of a PromptChoice instance; owns its nested sequence.</summary>
    public sealed class PromptChoiceOptionDefinition
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public ActionSequenceDefinition Sequence { get; set; } = new ActionSequenceDefinition();
    }

    /// <summary>
    /// Transfers flow out of the containing reusable Phase through one of that
    /// Phase's exported exits, saving resumable continuation state first.
    /// Scope: phase-level sequences only. The enclosing Session PhaseReference node
    /// projects the referenced exit as its Session-graph output socket.
    /// </summary>
    public sealed class PhaseGotoInstanceDefinition : ActionInstanceDefinition
    {
        public string PhaseExitId { get; set; } = "";
    }

    /// <summary>
    /// Transfers flow at the Session-graph level to the edge wired from this
    /// instance's unique output socket on its containing SessionDecision node,
    /// saving resumable continuation state first. Scope: session decision options only.
    /// </summary>
    public sealed class SessionGotoInstanceDefinition : ActionInstanceDefinition
    {
        /// <summary>Author-visible inline label rendered on its row and socket.</summary>
        public string Label { get; set; } = "";
    }

    /// <summary>Pops the newest saved continuation and resumes it exactly; empty stack is a clear runtime error.</summary>
    public sealed class ReturnInstanceDefinition : ActionInstanceDefinition
    {
    }

    /// <summary>Absolute terminal: discards the entire continuation stack and marks the run complete.</summary>
    public sealed class EndSessionInstanceDefinition : ActionInstanceDefinition
    {
    }
}
