using System;

namespace TruthCardGame.Content
{
    /// <summary>
    /// The owner contexts a sequence of Action Instances can live in. Used by the
    /// Action Type registry (Game.Core, Ticket 05) to make illegal GOTO kinds and
    /// other scope mismatches impossible to author.
    ///
    /// Flags so one Action Type may be legal in several scopes. A PromptChoice
    /// option's inner sequence inherits the enclosing execution context at runtime;
    /// the static declaration describes where the Choice itself may be authored.
    /// </summary>
    [Flags]
    public enum ActionOwnerScope
    {
        None = 0,

        /// <summary>Sequence owned by a Card (executed under a CardExecutor during a PhaseRun).</summary>
        CardSequence = 1 << 0,

        /// <summary>Sequence owned by a Phase-graph action node or PhaseDecision option.</summary>
        PhaseActionSequence = 1 << 1,

        /// <summary>Nested sequence inside a PromptChoice instance (inherits enclosing context).</summary>
        ChoiceOptionSequence = 1 << 2,

        /// <summary>Sequence owned by a SessionDecision option (session-level; no active PhaseRun required).</summary>
        SessionDecisionOptionSequence = 1 << 3,

        /// <summary>
        /// Sequence nested inside a PromptChoice that is itself nested below a
        /// SessionDecision option. It inherits session-safe actions but is not a
        /// direct SessionDecision option, so SessionGoto is deliberately absent.
        /// </summary>
        SessionDecisionPromptChoiceSequence = 1 << 4,

        All = CardSequence | PhaseActionSequence | ChoiceOptionSequence
            | SessionDecisionOptionSequence | SessionDecisionPromptChoiceSequence,
    }

    /// <summary>Centralizes the ownership rule for recursively nested PromptChoice sequences.</summary>
    public static class ActionOwnerScopes
    {
        public static ActionOwnerScope NestedPromptChoice(ActionOwnerScope enclosing)
        {
            return enclosing == ActionOwnerScope.SessionDecisionOptionSequence
                || enclosing == ActionOwnerScope.SessionDecisionPromptChoiceSequence
                ? ActionOwnerScope.SessionDecisionPromptChoiceSequence
                : enclosing;
        }
    }
}
