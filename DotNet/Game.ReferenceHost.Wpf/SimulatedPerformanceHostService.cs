using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Ticket 03's simulated presentation service. It performs no rendering, but
    /// it is a real <see cref="IPerformanceHost"/>: it validates that a requested
    /// staging is coherent against the generated catalog, acknowledges or
    /// rejects it, and writes a decision line naming the destination, the
    /// reusable operations, the selected acting and why anything was refused.
    ///
    /// This is what lets the Workbench explain a performance without Unity
    /// running, and it keeps the same accept/reject contract Unity will honour —
    /// an incoherent request is a loud failure, never a silent substitution.
    /// </summary>
    public sealed class SimulatedPerformanceHostService : IPerformanceHost
    {
        private readonly Action<string> _log;
        private readonly List<string> _decisions = new List<string>();

        public SimulatedPerformanceHostService(
            PresentationCatalogDefinition catalog,
            PerformanceActorState initialState,
            Action<string> log = null)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (initialState == null || string.IsNullOrEmpty(initialState.AnchorId))
                throw new ArgumentException("The simulated host needs a starting anchor.", nameof(initialState));
            InitialState = initialState;
            _log = log;
            State = initialState;
        }

        public PresentationCatalogDefinition Catalog { get; }

        public PerformanceActorState InitialState { get; }

        /// <summary>Where the simulated actor currently is; advances only on acceptance.</summary>
        public PerformanceActorState State { get; private set; }

        public IReadOnlyList<string> Decisions => _decisions;

        public Task<PerformanceExecutionResult> ExecuteAsync(
            PerformanceExecutionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null) throw new ArgumentNullException(nameof(request));

            string failure;
            switch (request.Kind)
            {
                case PerformanceRequestKind.Stop:
                    Record(request, $"released {State}");
                    State = new PerformanceActorState("", "");
                    return Task.FromResult(PerformanceExecutionResult.Accept(request.CorrelationId));

                case PerformanceRequestKind.RefreshActing:
                    failure = ValidateActing(request, expectStaging: false);
                    if (failure != null) return Task.FromResult(Reject(request, failure));
                    Record(request, $"acting refreshed, still at {State}");
                    return Task.FromResult(PerformanceExecutionResult.Accept(request.CorrelationId));

                default:
                    failure = ValidateActing(request, expectStaging: true);
                    if (failure != null) return Task.FromResult(Reject(request, failure));
                    State = new PerformanceActorState(request.AnchorId, request.PostureId);
                    Record(request, $"staged at {State}");
                    return Task.FromResult(PerformanceExecutionResult.Accept(request.CorrelationId));
            }
        }

        /// <summary>Returns the first incoherence, or null when the request is executable.</summary>
        private string ValidateActing(PerformanceExecutionRequest request, bool expectStaging)
        {
            if (expectStaging)
            {
                var anchor = Catalog.Anchors.FirstOrDefault(item => item.Id == request.AnchorId);
                if (anchor == null) return $"destination anchor '{request.AnchorId}' is not in the catalog";
                if (!anchor.SupportedPostureIds.Contains(request.PostureId))
                    return $"anchor '{anchor.Id}' does not support posture '{request.PostureId}'";

                var foundation = RequireIngredient(request.FoundationIngredientId, PresentationIngredientKinds.Foundation, out var foundationFailure);
                if (foundation == null) return foundationFailure;
                if (!foundation.SupportedPostureIds.Contains(request.PostureId))
                    return $"foundation '{foundation.Id}' does not support posture '{request.PostureId}'";
                if (!SupportsAnchor(foundation, anchor.Id))
                    return $"foundation '{foundation.Id}' is not attached to anchor '{anchor.Id}'";
            }
            else if (string.Equals(request.AnchorId, State.AnchorId, StringComparison.Ordinal) == false ||
                     string.Equals(request.PostureId, State.PostureId, StringComparison.Ordinal) == false)
            {
                return "a refresh may not relocate or re-pose the actor";
            }

            if (!request.UseBodyRest)
            {
                var body = RequireIngredient(request.BodyIngredientId, PresentationIngredientKinds.Body, out var bodyFailure);
                if (body == null) return bodyFailure;
                if (expectStaging && body.SupportedPostureIds.Count > 0 &&
                    !body.SupportedPostureIds.Contains(request.PostureId))
                    return $"body ingredient '{body.Id}' does not support posture '{request.PostureId}'";
                if (expectStaging && !SupportsAnchor(body, request.AnchorId))
                    return $"body ingredient '{body.Id}' is not available at anchor '{request.AnchorId}'";
            }

            var face = RequireIngredient(request.FaceIngredientId, PresentationIngredientKinds.Face, out var faceFailure);
            if (face == null) return faceFailure;
            if (face.OwnsHead != request.OwnsHead)
                return $"face ingredient '{face.Id}' declares OwnsHead={face.OwnsHead} but the request said {request.OwnsHead}";

            return null;
        }

        /// <summary>
        /// An ingredient that declares no anchor restriction is globally
        /// applicable; otherwise the destination anchor must be listed.
        /// </summary>
        private static bool SupportsAnchor(PresentationIngredientDefinition ingredient, string anchorId)
        {
            return ingredient.SupportedAnchorIds.Count == 0
                   || ingredient.SupportedAnchorIds.Contains(anchorId);
        }

        private PresentationIngredientDefinition RequireIngredient(string id, string kind, out string failure)
        {
            failure = null;
            var ingredient = Catalog.Ingredients.FirstOrDefault(item => item.Id == id);
            if (ingredient == null)
            {
                failure = $"{kind} ingredient '{id}' is not in the catalog";
                return null;
            }
            if (ingredient.Kind != kind)
            {
                failure = $"ingredient '{id}' is a {ingredient.Kind}, not a {kind}";
                return null;
            }
            if (!ingredient.Enabled)
            {
                failure = $"ingredient '{id}' is disabled";
                return null;
            }
            return ingredient;
        }

        private static PerformanceExecutionResult Reject(PerformanceExecutionRequest request, string reason)
        {
            return PerformanceExecutionResult.Reject(request.CorrelationId, reason);
        }

        private void Record(PerformanceExecutionRequest request, string outcome)
        {
            var operations = request.Operations.Count == 0
                ? "-"
                : string.Join(",", request.Operations.Select(operation => operation.ToString()));
            var line = request.Kind == PerformanceRequestKind.Stop
                ? $"SIM PERFORM {request.CorrelationId} stop: {outcome}"
                : $"SIM PERFORM {request.CorrelationId} {request.Kind} -> {request.AnchorId}/{request.PostureId} " +
                  $"ops=[{operations}] foundation={request.FoundationIngredientId} " +
                  $"body={(request.UseBodyRest ? "<rest>" : request.BodyIngredientId)} " +
                  $"face={request.FaceIngredientId} gaze={(request.OwnsHead ? "suspended" : "on player")} " +
                  $": {outcome}";
            _decisions.Add(line);
            _log?.Invoke(line);
        }
    }
}
