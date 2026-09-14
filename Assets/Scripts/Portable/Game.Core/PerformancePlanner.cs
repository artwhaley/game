using System;
using System.Collections.Generic;
using System.Linq;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// One viable destination/ingredient combination: the composed operations to
    /// reach the destination, the required foundation, the optional body gesture
    /// (or the explicit rest choice) and the required face.
    /// </summary>
    public sealed class PerformancePlanCandidate
    {
        public string AnchorId { get; set; } = "";
        public string PostureId { get; set; } = "";
        public List<PlannedPerformanceOperation> Operations { get; } = new List<PlannedPerformanceOperation>();
        public int Cost { get; set; }
        public string FoundationIngredientId { get; set; } = "";
        public string BodyIngredientId { get; set; } = "";
        public bool UseBodyRest { get; set; }
        public string FaceIngredientId { get; set; } = "";
        public bool OwnsHead { get; set; }

        /// <summary>Stable identity of the destination+path, used for deterministic tie-breaks.</summary>
        public string Signature =>
            AnchorId + "|" + PostureId + "|" + string.Join(",", Operations.Select(o => o.OperationId));
    }

    /// <summary>All viable plans plus the recorded reasons rejected destinations were excluded.</summary>
    public sealed class PerformancePlanSet
    {
        public List<PerformancePlanCandidate> Candidates { get; } = new List<PerformancePlanCandidate>();
        public List<string> Exclusions { get; } = new List<string>();
        public bool HasCandidates => Candidates.Count > 0;
    }

    /// <summary>
    /// Portable factored planning: state is (anchor, posture); reusable
    /// operations (stand, sit, move-while-standing) compose the shortest legal
    /// path; destination and expressive ingredients must be individually
    /// compatible. Missing required operations or a missing face exclude a plan
    /// — there is no teleport or unrelated-face fallback.
    /// </summary>
    public static class PerformancePlanner
    {
        public static PerformancePlanSet Plan(
            PresentationCatalogDefinition catalog,
            PerformanceActorState current,
            ConversationPerformanceEventDefinition performanceEvent)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (performanceEvent == null) throw new ArgumentNullException(nameof(performanceEvent));

            var set = new PerformancePlanSet();
            var anchors = new Dictionary<string, PresentationAnchorDefinition>(StringComparer.Ordinal);
            foreach (var anchor in catalog.Anchors ?? new List<PresentationAnchorDefinition>())
            {
                if (anchor != null && !string.IsNullOrEmpty(anchor.Id)) anchors[anchor.Id] = anchor;
            }

            if (anchors.Count == 0)
            {
                set.Exclusions.Add("catalog has no anchors");
                return set;
            }

            var standOp = FirstOperation(catalog.Operations, PresentationOperationKinds.Stand);
            var sitOp = FirstOperation(catalog.Operations, PresentationOperationKinds.Sit);
            var moveOp = FirstOperation(catalog.Operations, PresentationOperationKinds.MoveWhileStanding);

            var destinations = ResolveDestinations(catalog, anchors, current, performanceEvent, set);
            if (destinations.Count == 0) return set;

            foreach (var destination in destinations)
            {
                var anchor = anchors[destination];
                var postures = ResolveTargetPostures(anchor, current, performanceEvent, set);
                if (postures.Count == 0) continue;

                foreach (var posture in postures)
                {
                    if (!anchor.SupportedPostureIds.Contains(posture)) continue;

                    var operations = FindPath(catalog, anchor, anchors, current, destination, posture,
                        standOp, sitOp, moveOp);
                    if (operations == null)
                    {
                        set.Exclusions.Add(
                            $"destination '{destination}/{posture}': no legal operation path from '{(current == null ? "<unpositioned>" : current.ToString())}'");
                        continue;
                    }

                    var foundation = FindFirstIngredient(catalog, PresentationIngredientKinds.Foundation,
                        destination, posture, performanceEvent, requireTags: false);
                    if (foundation == null)
                    {
                        set.Exclusions.Add($"destination '{destination}/{posture}': no compatible foundation ingredient");
                        continue;
                    }

                    var faces = FindIngredients(catalog, PresentationIngredientKinds.Face,
                        destination, posture, performanceEvent).ToList();
                    if (faces.Count == 0)
                    {
                        set.Exclusions.Add($"destination '{destination}/{posture}': no matching face ingredient");
                        continue;
                    }

                    var bodies = FindIngredients(catalog, PresentationIngredientKinds.Body,
                        destination, posture, performanceEvent).ToList();

                    foreach (var face in faces)
                    {
                        if (bodies.Count == 0)
                        {
                            AddCandidate(set, destination, posture, operations, foundation, face,
                                body: null, standOp, sitOp, moveOp);
                        }
                        else
                        {
                            foreach (var body in bodies)
                            {
                                AddCandidate(set, destination, posture, operations, foundation, face,
                                    body, standOp, sitOp, moveOp);
                            }
                        }
                    }
                }
            }

            return set;
        }

        private static void AddCandidate(
            PerformancePlanSet set, string anchorId, string posture,
            List<PlannedPerformanceOperation> operations,
            PresentationIngredientDefinition foundation, PresentationIngredientDefinition face,
            PresentationIngredientDefinition body,
            PresentationOperationDefinition standOp, PresentationOperationDefinition sitOp,
            PresentationOperationDefinition moveOp)
        {
            var candidate = new PerformancePlanCandidate
            {
                AnchorId = anchorId,
                PostureId = posture,
                FoundationIngredientId = foundation.Id,
                FaceIngredientId = face.Id,
                BodyIngredientId = body?.Id ?? "",
                UseBodyRest = body == null,
                OwnsHead = body != null ? body.OwnsHead : face.OwnsHead,
                Cost = operations.Sum(o => OperationCost(o.OperationId, standOp, sitOp, moveOp)),
            };
            candidate.Operations.AddRange(operations);
            set.Candidates.Add(candidate);
        }

        private static int OperationCost(string operationId,
            PresentationOperationDefinition standOp, PresentationOperationDefinition sitOp,
            PresentationOperationDefinition moveOp)
        {
            if (standOp != null && standOp.Id == operationId) return standOp.Cost;
            if (sitOp != null && sitOp.Id == operationId) return sitOp.Cost;
            if (moveOp != null && moveOp.Id == operationId) return moveOp.Cost;
            return 1;
        }

        private static List<string> ResolveDestinations(
            PresentationCatalogDefinition catalog,
            Dictionary<string, PresentationAnchorDefinition> anchors,
            PerformanceActorState current,
            ConversationPerformanceEventDefinition performanceEvent,
            PerformancePlanSet set)
        {
            var allowed = new HashSet<string>(performanceEvent.AllowedAnchorIds ?? new List<string>(), StringComparer.Ordinal);
            var result = new List<string>();

            foreach (var anchor in catalog.Anchors)
            {
                if (anchor == null || string.IsNullOrEmpty(anchor.Id)) continue;
                if (allowed.Count > 0 && !allowed.Contains(anchor.Id)) continue;

                var include = true;
                switch (performanceEvent.StagingPolicy)
                {
                    case PerformanceStagingPolicy.Stay:
                        include = current == null || anchor.Id == current.AnchorId;
                        break;
                    case PerformanceStagingPolicy.ChooseCompatible:
                        break;
                    case PerformanceStagingPolicy.DifferentLocation:
                        var currentGroup = current != null && anchors.TryGetValue(current.AnchorId, out var currentAnchor)
                            ? LocationGroup(currentAnchor)
                            : current?.AnchorId ?? "";
                        include = current == null || !string.Equals(
                            LocationGroup(anchor), currentGroup, StringComparison.Ordinal);
                        break;
                    case PerformanceStagingPolicy.NamedLocation:
                        include = anchor.Id == (performanceEvent.NamedAnchorId ?? "");
                        break;
                }
                if (include) result.Add(anchor.Id);
            }

            if (result.Count == 0)
            {
                set.Exclusions.Add(
                    $"no destination anchor satisfies the '{performanceEvent.StagingPolicy}' staging policy");
            }
            return result;
        }

        private static List<string> ResolveTargetPostures(
            PresentationAnchorDefinition anchor,
            PerformanceActorState current,
            ConversationPerformanceEventDefinition performanceEvent,
            PerformancePlanSet set)
        {
            var allowed = performanceEvent.AllowedPostureIds ?? new List<string>();
            if (allowed.Count > 0)
            {
                var constrained = anchor.SupportedPostureIds.Where(allowed.Contains).ToList();
                if (constrained.Count == 0)
                {
                    set.Exclusions.Add(
                        $"anchor '{anchor.Id}' supports none of the event's allowed postures ({string.Join(",", allowed)})");
                }
                return constrained;
            }

            if (current != null && anchor.SupportedPostureIds.Contains(current.PostureId))
            {
                return new List<string> { current.PostureId };
            }
            return new List<string>(anchor.SupportedPostureIds);
        }

        private static string LocationGroup(PresentationAnchorDefinition anchor)
        {
            return string.IsNullOrEmpty(anchor.LocationGroup) ? anchor.Id : anchor.LocationGroup;
        }

        private static PresentationOperationDefinition FirstOperation(
            List<PresentationOperationDefinition> operations, string kind)
        {
            return (operations ?? new List<PresentationOperationDefinition>())
                .FirstOrDefault(o => o != null && o.Kind == kind);
        }

        private static bool Applicable(PresentationOperationDefinition operation, string anchorId)
        {
            return operation.ApplicableAnchorIds == null
                || operation.ApplicableAnchorIds.Count == 0
                || operation.ApplicableAnchorIds.Contains(anchorId);
        }

        private static PresentationIngredientDefinition FindFirstIngredient(
            PresentationCatalogDefinition catalog, string kind, string anchorId, string posture,
            ConversationPerformanceEventDefinition performanceEvent, bool requireTags)
        {
            foreach (var ingredient in catalog.Ingredients)
            {
                if (IsCompatible(ingredient, kind, anchorId, posture)
                    && (!requireTags || SatisfiesTags(ingredient, performanceEvent)))
                {
                    return ingredient;
                }
            }
            return null;
        }

        private static IEnumerable<PresentationIngredientDefinition> FindIngredients(
            PresentationCatalogDefinition catalog, string kind, string anchorId, string posture,
            ConversationPerformanceEventDefinition performanceEvent)
        {
            foreach (var ingredient in catalog.Ingredients)
            {
                if (IsCompatible(ingredient, kind, anchorId, posture)
                    && SatisfiesTags(ingredient, performanceEvent))
                {
                    yield return ingredient;
                }
            }
        }

        private static bool IsCompatible(
            PresentationIngredientDefinition ingredient, string kind, string anchorId, string posture)
        {
            if (ingredient == null || !ingredient.Enabled) return false;
            if (ingredient.Kind != kind) return false;
            if (!ingredient.SupportedPostureIds.Contains(posture)) return false;
            if (ingredient.SupportedAnchorIds.Count > 0 && !ingredient.SupportedAnchorIds.Contains(anchorId)) return false;
            return true;
        }

        private static bool SatisfiesTags(
            PresentationIngredientDefinition ingredient, ConversationPerformanceEventDefinition performanceEvent)
        {
            var required = performanceEvent.PerformanceTagIds;
            if (required == null || required.Count == 0) return true;
            if (performanceEvent.RequireAllTags)
            {
                foreach (var tag in required)
                {
                    if (!ingredient.PerformanceTagIds.Contains(tag)) return false;
                }
                return true;
            }
            foreach (var tag in required)
            {
                if (ingredient.PerformanceTagIds.Contains(tag)) return true;
            }
            return false;
        }

        // ---------- shortest legal operation path ----------

        private static List<PlannedPerformanceOperation> FindPath(
            PresentationCatalogDefinition catalog,
            PresentationAnchorDefinition destination,
            Dictionary<string, PresentationAnchorDefinition> anchors,
            PerformanceActorState current,
            string destinationAnchorId, string destinationPosture,
            PresentationOperationDefinition standOp, PresentationOperationDefinition sitOp,
            PresentationOperationDefinition moveOp)
        {
            if (current == null)
            {
                // Unpositioned: the first Stage establishes the destination directly.
                return new List<PlannedPerformanceOperation>();
            }

            var start = Key(current.AnchorId, current.PostureId);
            var goal = Key(destinationAnchorId, destinationPosture);
            if (start == goal) return new List<PlannedPerformanceOperation>();

            var distance = new Dictionary<string, int>(StringComparer.Ordinal) { [start] = 0 };
            var signature = new Dictionary<string, string>(StringComparer.Ordinal) { [start] = "" };
            var previous = new Dictionary<string, (string Previous, PlannedPerformanceOperation Op)>(StringComparer.Ordinal);
            var frontier = new List<string> { start };
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (frontier.Count > 0)
            {
                // Extract the lowest (cost, signature) state deterministically.
                var bestIndex = 0;
                for (var i = 1; i < frontier.Count; i++)
                {
                    var a = frontier[i];
                    var b = frontier[bestIndex];
                    if (distance[a] < distance[b]
                        || (distance[a] == distance[b]
                            && string.CompareOrdinal(signature[a], signature[b]) < 0))
                    {
                        bestIndex = i;
                    }
                }
                var node = frontier[bestIndex];
                frontier.RemoveAt(bestIndex);
                if (!visited.Add(node)) continue;
                if (node == goal) break;

                foreach (var edge in Neighbors(anchors, node, standOp, sitOp, moveOp))
                {
                    var nextCost = distance[node] + edge.Cost;
                    var nextSignature = signature[node] + ">" + edge.Operation.OperationId;
                    if (!distance.TryGetValue(edge.Key, out var known)
                        || nextCost < known
                        || (nextCost == known && string.CompareOrdinal(nextSignature, signature[edge.Key]) < 0))
                    {
                        distance[edge.Key] = nextCost;
                        signature[edge.Key] = nextSignature;
                        previous[edge.Key] = (node, edge.Operation);
                        frontier.Add(edge.Key);
                    }
                }
            }

            if (!previous.ContainsKey(goal)) return null;

            var path = new List<PlannedPerformanceOperation>();
            var cursor = goal;
            while (previous.TryGetValue(cursor, out var step))
            {
                path.Add(step.Op);
                cursor = step.Previous;
            }
            path.Reverse();
            return path;
        }

        private static IEnumerable<(string Key, int Cost, PlannedPerformanceOperation Operation)> Neighbors(
            Dictionary<string, PresentationAnchorDefinition> anchors, string node,
            PresentationOperationDefinition standOp, PresentationOperationDefinition sitOp,
            PresentationOperationDefinition moveOp)
        {
            var parts = node.Split('|');
            var anchorId = parts[0];
            var posture = parts.Length > 1 ? parts[1] : "";
            if (!anchors.TryGetValue(anchorId, out var anchor)) yield break;

            var supportsSitting = anchor.SupportedPostureIds.Contains(PresentationPostures.Sitting);
            var supportsStanding = anchor.SupportedPostureIds.Contains(PresentationPostures.Standing);

            if (posture == PresentationPostures.Sitting && supportsStanding
                && standOp != null && Applicable(standOp, anchorId))
            {
                yield return (Key(anchorId, PresentationPostures.Standing), standOp.Cost,
                    new PlannedPerformanceOperation
                    {
                        OperationId = standOp.Id,
                        Kind = standOp.Kind,
                        AnchorId = anchorId,
                        ResultPostureId = PresentationPostures.Standing,
                    });
            }

            if (posture == PresentationPostures.Standing && supportsSitting
                && sitOp != null && Applicable(sitOp, anchorId))
            {
                yield return (Key(anchorId, PresentationPostures.Sitting), sitOp.Cost,
                    new PlannedPerformanceOperation
                    {
                        OperationId = sitOp.Id,
                        Kind = sitOp.Kind,
                        AnchorId = anchorId,
                        ResultPostureId = PresentationPostures.Sitting,
                    });
            }

            if (posture == PresentationPostures.Standing && moveOp != null)
            {
                foreach (var connected in anchor.ConnectedAnchorIds)
                {
                    if (!anchors.TryGetValue(connected, out var target)) continue;
                    if (!target.SupportedPostureIds.Contains(PresentationPostures.Standing)) continue;
                    if (!Applicable(moveOp, connected)) continue;
                    yield return (Key(connected, PresentationPostures.Standing), moveOp.Cost,
                        new PlannedPerformanceOperation
                        {
                            OperationId = moveOp.Id,
                            Kind = moveOp.Kind,
                            AnchorId = connected,
                            ResultPostureId = PresentationPostures.Standing,
                        });
                }
            }
        }

        private static string Key(string anchorId, string postureId) => anchorId + "|" + postureId;
    }
}
