using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// One live execution of a Phase (created per PhaseReference entrance, per
    /// execution semantics: every entrance makes a fresh PhaseRun even when the
    /// same Phase is suspended elsewhere on the continuation stack). Owns the
    /// Phase-local progress, the Card RNG object, the current graph node, and
    /// (future) draw-history/cooldown state. Suspended runs keep their own RNG
    /// and progress untouched while other runs execute.
    /// </summary>
    public sealed class PhaseRun
    {
        /// <summary>Session PhaseReference node that created this run (GOTO resolution identity).</summary>
        public string PlacementNodeId { get; }

        public string PhaseId { get; }

        /// <summary>Phase-local progress; mutated only by IncrementProgress instances.</summary>
        public PhaseProgressState Progress { get; } = new PhaseProgressState();

        /// <summary>This run's own Card RNG — never shared with other runs.</summary>
        public IRandomSource CardRng { get; }

        /// <summary>Current graph node; null until the first step enters the Entry node.</summary>
        public GraphNodeDefinition CurrentNode { get; set; }

        /// <summary>Draw history for future cooldown/repetition rules; empty in v1.</summary>
        public List<string> DrawHistory { get; } = new List<string>();

        /// <summary>Card Action continuation suspended by WaitForContinue.</summary>
        public CardDefinition SuspendedCard { get; set; }

        /// <summary>Remaining points inside SuspendedCard's nested Action chain.</summary>
        public IReadOnlyList<ContinuationPoint> SuspendedCardChain { get; set; }

        /// <summary>Phase action/decision continuation suspended by WaitForContinue.</summary>
        public GraphNodeDefinition SuspendedActionLocus { get; set; }

        /// <summary>Remaining points for SuspendedActionLocus.</summary>
        public IReadOnlyList<ContinuationPoint> SuspendedActionChain { get; set; }

        public PhaseRun(string placementNodeId, string phaseId, IRandomSource cardRng)
        {
            PlacementNodeId = placementNodeId ?? "";
            PhaseId = phaseId ?? "";
            CardRng = cardRng ?? new SystemRandomSource();
        }
    }

    /// <summary>What one Phase RunUntilYield operation produced.</summary>
    public enum PhaseAdvanceOutcome
    {
        /// <summary>An authored WaitForContinue suspended this Phase run.</summary>
        YieldedForContinue,

        /// <summary>A flow action fired (PhaseGoto / SessionGoto / Return / EndSession) — the session VM resolves it.</summary>
        Transferred,

        /// <summary>The phase graph reached a terminal state without an explicit transfer.</summary>
        Completed,

        /// <summary>Runtime content error (dead edge, no card, loop guard, missing subtype).</summary>
        Error,
    }

    public sealed class PhaseAdvanceResult
    {
        public PhaseAdvanceOutcome Outcome { get; }
        public CardDefinition Card { get; }
        public ActionExecutionResult Transfer { get; }
        public string ErrorMessage { get; }

        private PhaseAdvanceResult(PhaseAdvanceOutcome outcome, CardDefinition card, ActionExecutionResult transfer, string error)
        {
            Outcome = outcome;
            Card = card;
            Transfer = transfer;
            ErrorMessage = error;
        }

        public static readonly PhaseAdvanceResult YieldedForContinue =
            new PhaseAdvanceResult(PhaseAdvanceOutcome.YieldedForContinue, null, null, null);

        public static PhaseAdvanceResult Transferred(ActionExecutionResult transfer)
        {
            return new PhaseAdvanceResult(PhaseAdvanceOutcome.Transferred, null, transfer, null);
        }

        public static readonly PhaseAdvanceResult Completed =
            new PhaseAdvanceResult(PhaseAdvanceOutcome.Completed, null, null, null);

        public static PhaseAdvanceResult Error(string message)
        {
            return new PhaseAdvanceResult(PhaseAdvanceOutcome.Error, null, null, message);
        }
    }
}
