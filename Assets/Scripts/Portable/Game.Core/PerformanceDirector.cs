using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>The expressive ingredients currently established for the actor.</summary>
    public sealed class PerformanceActingSelection
    {
        public string FoundationIngredientId { get; set; } = "";
        public string BodyIngredientId { get; set; } = "";
        public bool UseBodyRest { get; set; }
        public string FaceIngredientId { get; set; } = "";
        public bool OwnsHead { get; set; }

        public override string ToString()
        {
            return $"foundation={FoundationIngredientId} body={(UseBodyRest ? "<rest>" : BodyIngredientId)} " +
                   $"face={FaceIngredientId}";
        }
    }

    /// <summary>
    /// Session-scoped performance state and async host boundary. It owns the
    /// committed (anchor, posture), the active event, the selected acting, and
    /// the dedicated performance RNG. It plans legal staging, selects compatible
    /// ingredients with immediate repeat exclusion, and commits state only after
    /// the host accepts. Core has no delta-time API here: Unity keeps rendering
    /// the accepted state until it is replaced or stopped.
    /// </summary>
    public sealed class PerformanceDirector
    {
        private readonly PresentationCatalogDefinition _catalog;
        private readonly IPerformanceHost _host;
        private readonly IRandomSource _rng;
        private readonly IGameLog _log;

        private PerformanceActorState _committed;
        private PerformanceActingSelection _acting;
        private ConversationPerformanceEventDefinition _activeEvent;
        private string _lastBodyIngredientId;
        private string _lastFaceIngredientId;
        private int _correlationCounter;

        public PerformanceDirector(
            PresentationCatalogDefinition catalog,
            IPerformanceHost host,
            IRandomSource rng,
            IGameLog log)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _log = log ?? new NullGameLog();

            var initial = host.InitialState;
            if (initial == null || string.IsNullOrEmpty(initial.AnchorId))
            {
                throw new InvalidOperationException(
                    "PerformanceHost declares no starting anchor/posture; the run setup must supply one.");
            }
            var anchor = FindAnchor(initial.AnchorId);
            if (anchor == null)
            {
                throw new InvalidOperationException(
                    $"PerformanceHost starting anchor '{initial.AnchorId}' is not in the presentation catalog.");
            }
            if (!anchor.SupportedPostureIds.Contains(initial.PostureId))
            {
                throw new InvalidOperationException(
                    $"PerformanceHost starting posture '{initial.PostureId}' is not supported by anchor '{anchor.Id}'.");
            }
            _committed = initial;
        }

        public PerformanceActorState CommittedState => _committed;
        public string ActiveEventId => _activeEvent?.Id ?? "";
        public PerformanceActingSelection Acting => _acting;

        /// <summary>
        /// Perform(eventId): resolve a finite plan, select compatible acting, and
        /// await host acceptance. Always establishes fresh acting; a missing
        /// performance service or an unviable plan is a loud error.
        /// </summary>
        public async Task PerformAsync(ConversationPerformanceEventDefinition performanceEvent, CancellationToken cancellationToken)
        {
            if (performanceEvent == null) throw new ArgumentNullException(nameof(performanceEvent));

            var plan = PerformancePlanner.Plan(_catalog, _committed, performanceEvent);
            if (!plan.HasCandidates)
            {
                throw new InvalidOperationException(
                    $"Performance event '{performanceEvent.Id}' has no viable plan " +
                    $"(state {_committed}): {string.Join("; ", plan.Exclusions)}");
            }

            var destination = SelectDestination(plan);
            var (face, body) = SelectActing(destination);
            var anchor = destination[0].AnchorId;
            var posture = destination[0].PostureId;

            var request = new PerformanceExecutionRequest
            {
                CorrelationId = "perf-" + (++_correlationCounter),
                Kind = PerformanceRequestKind.Stage,
                AnchorId = anchor,
                PostureId = posture,
                FoundationIngredientId = destination[0].FoundationIngredientId,
                FaceIngredientId = face,
                BodyIngredientId = body ?? "",
                UseBodyRest = body == null,
                OwnsHead = destination.Find(c => c.FaceIngredientId == face
                    && string.Equals(c.BodyIngredientId, body ?? "", StringComparison.Ordinal))?.OwnsHead ?? false,
            };
            request.Operations.AddRange(destination[0].Operations);

            _log.Info(
                $"PERFORM event '{performanceEvent.Id}' -> {anchor}/{posture} " +
                $"ops=[{string.Join(",", request.Operations.Select(o => o.Kind))}] " +
                $"face={face} body={(body == null ? "<rest>" : body)}");

            await CommitAsync(request, cancellationToken);

            _activeEvent = performanceEvent;
            _lastFaceIngredientId = face;
            _lastBodyIngredientId = body;
        }

        /// <summary>
        /// Automatic refresh immediately before blocking dialogue. Retains the
        /// current anchor/posture and re-selects expressive acting from the
        /// active event. A disabled refresh flag or no active event is a no-op —
        /// dialogue presentation is then unchanged.
        /// </summary>
        public Task RefreshAtDialogueStartAsync(CancellationToken cancellationToken)
        {
            if (_activeEvent == null || !_activeEvent.RefreshAtDialogueStart)
            {
                return Task.CompletedTask;
            }
            return RefreshCoreAsync(cancellationToken);
        }

        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            if (_acting == null) return;

            var actingOnlyEvent = ActingOnly(_activeEvent, _committed);
            var plan = PerformancePlanner.Plan(_catalog, _committed, actingOnlyEvent);
            if (!plan.HasCandidates)
            {
                // A refresh cannot move or re-pose, so an unviable acting-only
                // plan must not silently keep an incompatible selection.
                throw new InvalidOperationException(
                    $"Performance refresh for event '{_activeEvent.Id}' has no compatible acting at {_committed}: " +
                    string.Join("; ", plan.Exclusions));
            }

            var destination = SelectDestination(plan);
            var (face, body) = SelectActing(destination);

            var request = new PerformanceExecutionRequest
            {
                CorrelationId = "perf-" + (++_correlationCounter),
                Kind = PerformanceRequestKind.RefreshActing,
                AnchorId = _committed.AnchorId,
                PostureId = _committed.PostureId,
                FoundationIngredientId = _acting.FoundationIngredientId,
                FaceIngredientId = face,
                BodyIngredientId = body ?? "",
                UseBodyRest = body == null,
                OwnsHead = destination.Find(c => c.FaceIngredientId == face
                    && string.Equals(c.BodyIngredientId, body ?? "", StringComparison.Ordinal))?.OwnsHead ?? false,
            };

            _log.Info(
                $"PERFORM refresh event '{_activeEvent.Id}' at {_committed} " +
                $"face={face} body={(body == null ? "<rest>" : body)}");

            await CommitAsync(request, cancellationToken);
            _lastFaceIngredientId = face;
            _lastBodyIngredientId = body;
        }

        /// <summary>Stops presentation and clears the active event. Safe when nothing was staged.</summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _activeEvent = null;
            _acting = null;
            if (!_staged) return;

            var request = new PerformanceExecutionRequest
            {
                CorrelationId = "perf-" + (++_correlationCounter),
                Kind = PerformanceRequestKind.Stop,
            };
            var result = await _host.ExecuteAsync(request, cancellationToken);
            if (result == null || !result.Accepted)
            {
                throw new InvalidOperationException(
                    $"Performance stop was not accepted: {result?.FailureReason ?? "no result"}");
            }
            _staged = false;
        }

        private bool _staged;

        private async Task CommitAsync(PerformanceExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await _host.ExecuteAsync(request, cancellationToken);
            if (result == null || !result.Accepted)
            {
                throw new InvalidOperationException(
                    $"Performance request '{request.CorrelationId}' ({request.Kind}) was rejected: " +
                    $"{result?.FailureReason ?? "no result returned"}. " +
                    "Committed state is unchanged; this is a host failure, not a silent fallback.");
            }

            // Commit only after acceptance: a failed or canceled request never
            // claims arrival.
            _committed = new PerformanceActorState(request.AnchorId, request.PostureId);
            _acting = new PerformanceActingSelection
            {
                FoundationIngredientId = request.FoundationIngredientId,
                BodyIngredientId = request.BodyIngredientId,
                UseBodyRest = request.UseBodyRest,
                FaceIngredientId = request.FaceIngredientId,
                OwnsHead = request.OwnsHead,
            };
            _staged = true;
        }

        /// <summary>
        /// Deterministic destination: the lowest-cost plan, ties broken by the
        /// stable destination+path signature. Randomness applies to expressive
        /// ingredient selection only.
        /// </summary>
        private static List<PerformancePlanCandidate> SelectDestination(PerformancePlanSet plan)
        {
            var minCost = plan.Candidates.Min(c => c.Cost);
            var best = plan.Candidates
                .Where(c => c.Cost == minCost)
                .OrderBy(c => c.Signature, StringComparer.Ordinal)
                .First();
            return plan.Candidates
                .Where(c => c.Cost == minCost && string.Equals(c.Signature, best.Signature, StringComparison.Ordinal))
                .ToList();
        }

        /// <summary>
        /// Uniform random expressive selection over a stable candidate order,
        /// excluding the immediately previous choice when another legal option
        /// exists. A body gesture is optional; its absence is the explicit rest
        /// choice, never a silent substitution.
        /// </summary>
        private (string Face, string Body) SelectActing(List<PerformancePlanCandidate> destination)
        {
            var faces = destination
                .Select(c => c.FaceIngredientId)
                .Distinct()
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (faces.Count > 1 && _lastFaceIngredientId != null) faces.Remove(_lastFaceIngredientId);
            var face = faces[_rng.NextInt(0, faces.Count)];

            var bodies = destination
                .Where(c => !c.UseBodyRest)
                .Select(c => c.BodyIngredientId)
                .Distinct()
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            string body = null;
            if (bodies.Count > 0)
            {
                if (bodies.Count > 1 && _lastBodyIngredientId != null) bodies.Remove(_lastBodyIngredientId);
                body = bodies[_rng.NextInt(0, bodies.Count)];
            }
            return (face, body);
        }

        private static ConversationPerformanceEventDefinition ActingOnly(
            ConversationPerformanceEventDefinition source, PerformanceActorState state)
        {
            var clone = new ConversationPerformanceEventDefinition
            {
                Id = source.Id,
                Name = source.Name,
                RequireAllTags = source.RequireAllTags,
                StagingPolicy = PerformanceStagingPolicy.Stay,
                RefreshAtDialogueStart = source.RefreshAtDialogueStart,
            };
            clone.PerformanceTagIds.AddRange(source.PerformanceTagIds);
            clone.AllowedAnchorIds.Add(state.AnchorId);
            clone.AllowedPostureIds.Add(state.PostureId);
            return clone;
        }

        private PresentationAnchorDefinition FindAnchor(string anchorId)
        {
            foreach (var anchor in _catalog.Anchors)
            {
                if (anchor != null && string.Equals(anchor.Id, anchorId, StringComparison.Ordinal)) return anchor;
            }
            return null;
        }
    }
}
