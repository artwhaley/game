using System.Collections.Generic;
using System.Linq;
using TruthCardGame.Content;
using TruthCardGame.Core;
using UnityEngine;

namespace TruthCardGame.Performance
{
    /// <summary>
    /// What the Unity host must actually do for one correlated request, fully
    /// resolved: ordered transition clips, the foundation clip for the
    /// destination posture, the body overlay (null meaning the explicit rest
    /// choice), the exported facial control and whether gaze must be suspended.
    ///
    /// Scene transforms are deliberately absent: anchors are scene objects and
    /// cannot be referenced from a registry asset, so the host resolves them.
    /// </summary>
    public sealed class PerformanceStagingPlan
    {
        public PerformanceRequestKind Kind { get; set; }
        public string AnchorId { get; set; } = "";
        public string PostureId { get; set; } = "";

        public List<AnimationClip> OperationClips { get; } = new List<AnimationClip>();
        public List<string> OperationKinds { get; } = new List<string>();

        public AnimationClip FoundationClip { get; set; }
        public AnimationClip OverlayClip { get; set; }

        public string FaceControl { get; set; } = "";
        public float FaceWeight { get; set; } = 100f;

        /// <summary>True when the selected acting owns the head, so gaze is suspended.</summary>
        public bool SuspendGaze { get; set; }

        public string FoundationIngredientId { get; set; } = "";
        public string BodyIngredientId { get; set; } = "";
        public string FaceIngredientId { get; set; } = "";

        /// <summary>Human-readable resolution trace, for the host's decision log.</summary>
        public List<string> Diagnostics { get; } = new List<string>();

        public string Describe()
        {
            var operations = OperationKinds.Count == 0 ? "-" : string.Join(",", OperationKinds);
            return $"{Kind} -> {AnchorId}/{PostureId} ops=[{operations}] " +
                   $"foundation={(FoundationClip == null ? "<keep>" : FoundationClip.name)} " +
                   $"body={(OverlayClip == null ? "<rest>" : OverlayClip.name)} " +
                   $"face={(string.IsNullOrEmpty(FaceControl) ? "<none>" : FaceControl)} " +
                   $"gaze={(SuspendGaze ? "suspended" : "on player")}";
        }
    }

    /// <summary>
    /// Resolves a semantic request against the Unity registry and the generated
    /// read-only catalog. Pure and engine-light on purpose: it decides what to
    /// do and why, and owns every rejection reason, so the MonoBehaviour host
    /// above it only applies an already-validated plan.
    ///
    /// An unresolved ingredient, a missing clip binding or a Core/host
    /// disagreement about head ownership is a named failure — never a silent
    /// substitution or a partial staging.
    /// </summary>
    public static class PerformanceStagingResolver
    {
        public static bool TryResolve(
            PerformanceExecutionRequest request,
            PerformanceRegistry registry,
            PresentationCatalogDefinition catalog,
            out PerformanceStagingPlan plan,
            out string failure)
        {
            plan = null;
            failure = null;

            if (request == null)
            {
                failure = "no performance request";
                return false;
            }
            if (registry == null)
            {
                failure = "the scene has no performance registry";
                return false;
            }
            if (catalog == null)
            {
                failure = "no generated presentation catalog is loaded";
                return false;
            }

            var resolved = new PerformanceStagingPlan
            {
                Kind = request.Kind,
                AnchorId = request.AnchorId ?? "",
                PostureId = request.PostureId ?? "",
            };

            if (request.Kind == PerformanceRequestKind.Stop)
            {
                resolved.Diagnostics.Add("stop: release presentation");
                plan = resolved;
                return true;
            }

            if (request.Kind == PerformanceRequestKind.Stage)
            {
                if (!TryResolveStaging(request, registry, catalog, resolved, out failure)) return false;
            }

            if (!TryResolveActing(request, registry, catalog, resolved, out failure)) return false;

            plan = resolved;
            return true;
        }

        /// <summary>Destination anchor, posture and the ordered reusable operations that reach it.</summary>
        private static bool TryResolveStaging(
            PerformanceExecutionRequest request,
            PerformanceRegistry registry,
            PresentationCatalogDefinition catalog,
            PerformanceStagingPlan plan,
            out string failure)
        {
            failure = null;

            var anchor = FindAnchor(catalog, request.AnchorId);
            if (anchor == null)
            {
                failure = $"destination anchor '{request.AnchorId}' is not declared by the generated catalog";
                return false;
            }
            if (!anchor.SupportedPostureIds.Contains(request.PostureId))
            {
                failure = $"anchor '{anchor.Id}' does not support posture '{request.PostureId}'";
                return false;
            }

            var foundation = FindCatalogIngredient(
                catalog, request.FoundationIngredientId, PresentationIngredientKinds.Foundation, out failure);
            if (foundation == null) return false;

            var foundationEntry = FindIngredient(registry, request.FoundationIngredientId);
            if (foundationEntry == null)
            {
                failure = $"foundation ingredient '{request.FoundationIngredientId}' has no registry entry";
                return false;
            }
            if (foundationEntry.Clip == null)
            {
                failure =
                    $"foundation ingredient '{foundationEntry.IngredientId}' has no Unity clip binding; " +
                    "rebind it in the performance registry";
                return false;
            }
            if (!foundationEntry.SupportedPostureIds.Contains(request.PostureId))
            {
                failure =
                    $"foundation ingredient '{foundationEntry.IngredientId}' declares no support for posture '{request.PostureId}'";
                return false;
            }

            plan.FoundationIngredientId = foundationEntry.IngredientId;
            plan.FoundationClip = foundationEntry.Clip;
            plan.Diagnostics.Add($"foundation {foundationEntry.IngredientId} ({foundationEntry.Clip.name})");

            for (var index = 0; index < request.Operations.Count; index++)
            {
                var operation = request.Operations[index];
                if (operation == null)
                {
                    failure = $"operation {index} of the plan is empty";
                    return false;
                }

                var entry = FindOperation(registry, operation.OperationId);
                if (entry == null)
                {
                    failure = $"operation '{operation.OperationId}' has no registry entry";
                    return false;
                }
                if (entry.Clip == null)
                {
                    failure =
                        $"operation '{entry.OperationId}' ({entry.Kind}) has no Unity clip binding, so the composed " +
                        "path cannot be executed; rebind it in the performance registry";
                    return false;
                }

                plan.OperationClips.Add(entry.Clip);
                plan.OperationKinds.Add(entry.Kind);
                plan.Diagnostics.Add($"operation {entry.Kind} ({entry.Clip.name})");
            }

            return true;
        }

        /// <summary>Body overlay (or explicit rest) and the mandatory facial preset.</summary>
        private static bool TryResolveActing(
            PerformanceExecutionRequest request,
            PerformanceRegistry registry,
            PresentationCatalogDefinition catalog,
            PerformanceStagingPlan plan,
            out string failure)
        {
            failure = null;

            var expectedOwnsHead = false;

            if (!request.UseBodyRest)
            {
                var body = FindCatalogIngredient(
                    catalog, request.BodyIngredientId, PresentationIngredientKinds.Body, out failure);
                if (body == null) return false;

                var bodyEntry = FindIngredient(registry, request.BodyIngredientId);
                if (bodyEntry == null)
                {
                    failure = $"body ingredient '{request.BodyIngredientId}' has no registry entry";
                    return false;
                }
                if (bodyEntry.Clip == null)
                {
                    failure = $"body ingredient '{bodyEntry.IngredientId}' has no Unity clip binding";
                    return false;
                }
                if (request.Kind == PerformanceRequestKind.Stage &&
                    bodyEntry.SupportedPostureIds.Count > 0 &&
                    !bodyEntry.SupportedPostureIds.Contains(request.PostureId))
                {
                    failure =
                        $"body ingredient '{bodyEntry.IngredientId}' declares no support for posture '{request.PostureId}'";
                    return false;
                }

                plan.OverlayClip = bodyEntry.Clip;
                plan.BodyIngredientId = bodyEntry.IngredientId;
                expectedOwnsHead = bodyEntry.OwnsHead;
                plan.Diagnostics.Add($"body {bodyEntry.IngredientId} ({bodyEntry.Clip.name})");
            }
            else
            {
                plan.Diagnostics.Add("body <rest>: no gesture matched, foundation is the pose");
            }

            var face = FindCatalogIngredient(
                catalog, request.FaceIngredientId, PresentationIngredientKinds.Face, out failure);
            if (face == null) return false;

            var faceEntry = FindIngredient(registry, request.FaceIngredientId);
            if (faceEntry == null)
            {
                failure = $"face ingredient '{request.FaceIngredientId}' has no registry entry";
                return false;
            }
            if (string.IsNullOrEmpty(faceEntry.FaceControlName))
            {
                failure = $"face ingredient '{faceEntry.IngredientId}' has no exported facial control bound";
                return false;
            }
            if (faceEntry.FaceWeight <= 0f)
            {
                failure =
                    $"face ingredient '{faceEntry.IngredientId}' is bound at weight {faceEntry.FaceWeight}, " +
                    "which would present nothing";
                return false;
            }
            if (request.UseBodyRest) expectedOwnsHead = faceEntry.OwnsHead;

            // Core derives head ownership from the catalog it planned against. A
            // disagreement means the two sides read different content, so it is
            // reported rather than resolved in either direction.
            if (expectedOwnsHead != request.OwnsHead)
            {
                failure =
                    $"head ownership disagrees: the catalog declares OwnsHead={expectedOwnsHead} for the selected " +
                    $"acting but Core asked for {request.OwnsHead}";
                return false;
            }

            plan.FaceControl = faceEntry.FaceControlName;
            plan.FaceWeight = faceEntry.FaceWeight;
            plan.FaceIngredientId = faceEntry.IngredientId;
            plan.SuspendGaze = request.OwnsHead;
            plan.Diagnostics.Add($"face {faceEntry.IngredientId} ({faceEntry.FaceControlName} @ {faceEntry.FaceWeight:0})");
            return true;
        }

        private static PresentationAnchorDefinition FindAnchor(
            PresentationCatalogDefinition catalog, string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId)) return null;
            foreach (var anchor in catalog.Anchors)
            {
                if (anchor != null && anchor.Id == anchorId) return anchor;
            }
            return null;
        }

        /// <summary>An ingredient the generated catalog declares, enabled, of the expected kind.</summary>
        private static PresentationIngredientDefinition FindCatalogIngredient(
            PresentationCatalogDefinition catalog, string ingredientId, string kind, out string failure)
        {
            failure = null;
            if (string.IsNullOrEmpty(ingredientId))
            {
                failure = $"the plan names no {kind} ingredient";
                return null;
            }

            PresentationIngredientDefinition found = null;
            foreach (var ingredient in catalog.Ingredients)
            {
                if (ingredient != null && ingredient.Id == ingredientId)
                {
                    found = ingredient;
                    break;
                }
            }

            if (found == null)
            {
                failure = $"{kind} ingredient '{ingredientId}' is not declared by the generated catalog";
                return null;
            }
            if (found.Kind != kind)
            {
                failure = $"ingredient '{ingredientId}' is a {found.Kind}, not a {kind}";
                return null;
            }
            if (!found.Enabled)
            {
                failure = $"{kind} ingredient '{ingredientId}' is disabled in the generated catalog";
                return null;
            }
            return found;
        }

        private static PerformanceIngredientEntry FindIngredient(PerformanceRegistry registry, string ingredientId)
        {
            if (string.IsNullOrEmpty(ingredientId)) return null;
            foreach (var ingredient in registry.Ingredients)
            {
                if (ingredient != null && ingredient.IngredientId == ingredientId) return ingredient;
            }
            return null;
        }

        private static PerformanceOperationEntry FindOperation(PerformanceRegistry registry, string operationId)
        {
            if (string.IsNullOrEmpty(operationId)) return null;
            foreach (var operation in registry.Operations)
            {
                if (operation != null && operation.OperationId == operationId) return operation;
            }
            return null;
        }
    }
}
