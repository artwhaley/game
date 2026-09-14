using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Core's current physical state: the anchor the actor occupies and the
    /// posture it holds. Generated search states are these pairs internally;
    /// they are never separately authored records.
    /// </summary>
    public sealed class PerformanceActorState
    {
        public string AnchorId { get; }
        public string PostureId { get; }

        public PerformanceActorState(string anchorId, string postureId)
        {
            AnchorId = anchorId ?? "";
            PostureId = postureId ?? "";
        }

        public override string ToString() => AnchorId + "/" + PostureId;
    }

    /// <summary>What Core is asking the host to do.</summary>
    public enum PerformanceRequestKind
    {
        /// <summary>Move/pose to a destination and establish fresh acting.</summary>
        Stage,

        /// <summary>Change expressive acting only; retain location and posture.</summary>
        RefreshActing,

        /// <summary>Stop presentation and release its state.</summary>
        Stop,
    }

    /// <summary>One reusable operation Core composed into a plan.</summary>
    public sealed class PlannedPerformanceOperation
    {
        public string OperationId { get; set; } = "";
        public string Kind { get; set; } = "";
        public string AnchorId { get; set; } = "";
        public string ResultPostureId { get; set; } = "";

        public override string ToString() => Kind + ":" + AnchorId + "->" + ResultPostureId;
    }

    /// <summary>
    /// A correlated semantic request. Core submits the composed operations plus
    /// the selected ingredient IDs; Unity executes movement, pose, overlay and
    /// gaze, then acknowledges whether the requested staging is coherent. There
    /// is deliberately no streaming command protocol: a request either commits
    /// on acceptance or reports failure.
    /// </summary>
    public sealed class PerformanceExecutionRequest
    {
        public string CorrelationId { get; set; } = "";
        public PerformanceRequestKind Kind { get; set; } = PerformanceRequestKind.Stage;

        /// <summary>Destination anchor (empty for RefreshActing/Stop).</summary>
        public string AnchorId { get; set; } = "";

        /// <summary>Destination posture (empty for RefreshActing/Stop).</summary>
        public string PostureId { get; set; } = "";

        public List<PlannedPerformanceOperation> Operations { get; } = new List<PlannedPerformanceOperation>();

        /// <summary>Foundation ingredient for the destination posture; always present for Stage.</summary>
        public string FoundationIngredientId { get; set; } = "";

        /// <summary>Body gesture ingredient, or empty when remaining at foundation.</summary>
        public string BodyIngredientId { get; set; } = "";

        /// <summary>Explicit V1 rest choice: no body gesture matched, so remain at foundation.</summary>
        public bool UseBodyRest { get; set; }

        /// <summary>Required expressive face ingredient.</summary>
        public string FaceIngredientId { get; set; } = "";

        /// <summary>True when the selected acting owns the head and suspends gaze.</summary>
        public bool OwnsHead { get; set; }
    }

    /// <summary>
    /// The host's acknowledgement. A rejected or canceled request must never be
    /// treated as arrival, so Core commits the new state only on Accepted.
    /// </summary>
    public sealed class PerformanceExecutionResult
    {
        public bool Accepted { get; private set; }
        public string CorrelationId { get; private set; } = "";
        public string FailureReason { get; private set; } = "";

        public static PerformanceExecutionResult Accept(string correlationId)
        {
            return new PerformanceExecutionResult { Accepted = true, CorrelationId = correlationId ?? "" };
        }

        public static PerformanceExecutionResult Reject(string correlationId, string reason)
        {
            return new PerformanceExecutionResult
            {
                Accepted = false,
                CorrelationId = correlationId ?? "",
                FailureReason = reason ?? "",
            };
        }
    }

    /// <summary>
    /// The Unity (or simulated) presentation host. It owns rendering — Animator/
    /// Playables, blends, IK, gaze, blink/breath and purely visual lifetime —
    /// and exposes the generated read-only catalog Core plans against. Core never
    /// receives delta time, mixer weights or frame ticks through this boundary.
    /// </summary>
    public interface IPerformanceHost
    {
        /// <summary>The current validated read-only presentation catalog.</summary>
        PresentationCatalogDefinition Catalog { get; }

        /// <summary>The starting anchor/posture declared by the run's setup.</summary>
        PerformanceActorState InitialState { get; }

        /// <summary>
        /// Executes one correlated request. Completion means the requested
        /// staging/acting is coherent. Cancellation propagates as cancellation;
        /// a failure must be reported, never silently absorbed.
        /// </summary>
        Task<PerformanceExecutionResult> ExecuteAsync(
            PerformanceExecutionRequest request, CancellationToken cancellationToken);
    }
}
