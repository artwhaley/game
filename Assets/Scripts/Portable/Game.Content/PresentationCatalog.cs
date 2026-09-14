using System;
using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// The single explicit vocabulary of presentation ingredient kinds.
    /// Foundation ingredients supply the posture base (standing/sitting idle);
    /// body ingredients are masked gestures over a compatible foundation; face
    /// ingredients are expression presets. Unity owns membership and
    /// compatibility; the catalog only projects the facts.
    /// </summary>
    public static class PresentationIngredientKinds
    {
        public const string Foundation = "foundation";
        public const string Body = "body";
        public const string Face = "face";

        public static bool IsKnown(string kind)
        {
            return kind == Foundation || kind == Body || kind == Face;
        }
    }

    /// <summary>
    /// The reusable transition operations Core composes. These are portable
    /// semantic capabilities, not scene objects or coordinates: Unity binds each
    /// operation kind to rig transitions and furniture calibration.
    /// </summary>
    public static class PresentationOperationKinds
    {
        /// <summary>Sitting anchor -> standing at the same anchor. Never inferred from a reversed clip.</summary>
        public const string Stand = "stand";

        /// <summary>Standing -> sitting at a sit-capable anchor.</summary>
        public const string Sit = "sit";

        /// <summary>Standing anchor -> connected standing anchor.</summary>
        public const string MoveWhileStanding = "move_while_standing";

        public static bool IsKnown(string kind)
        {
            return kind == Stand || kind == Sit || kind == MoveWhileStanding;
        }
    }

    /// <summary>The small V1 posture vocabulary. Broader postures follow observed needs, not speculation.</summary>
    public static class PresentationPostures
    {
        public const string Standing = "standing";
        public const string Sitting = "sitting";

        public static bool IsKnown(string posture)
        {
            return posture == Standing || posture == Sitting;
        }
    }

    /// <summary>
    /// One enabled presentation ingredient projected into the read-only
    /// catalog. It carries the stable ingredient ID, its kind, its semantic
    /// Performance Tag membership, the postures/anchors the artist confirmed as
    /// compatible, and whether the ingredient exceptionally owns the head
    /// (suspending gaze). No Unity objects, bone names or coordinates.
    /// </summary>
    public sealed class PresentationIngredientDefinition
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Kind { get; set; } = PresentationIngredientKinds.Body;
        public bool Enabled { get; set; }

        /// <summary>Semantic Performance Tag IDs (WPF/SQLite-owned vocabulary).</summary>
        public List<string> PerformanceTagIds { get; } = new List<string>();

        /// <summary>Postures the artist confirmed this ingredient supports.</summary>
        public List<string> SupportedPostureIds { get; } = new List<string>();

        /// <summary>Optional anchor restriction; empty means compatible with any anchor supporting the posture.</summary>
        public List<string> SupportedAnchorIds { get; } = new List<string>();

        /// <summary>Exceptional head-owning gesture: suspends gaze while active.</summary>
        public bool OwnsHead { get; set; }
    }

    /// <summary>
    /// One physical anchor (room center, door, chair A, chair B, ...). Anchors
    /// declare the postures they support and the anchors they connect to for
    /// stand-move-sit travel. Adding an equivalent sit-capable anchor needs an
    /// anchor, connections and calibration — never copies of every conversion.
    /// </summary>
    public sealed class PresentationAnchorDefinition
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";

        /// <summary>Location grouping for the DifferentLocation staging policy; empty means the anchor is its own group.</summary>
        public string LocationGroup { get; set; } = "";

        public List<string> SupportedPostureIds { get; } = new List<string>();

        /// <summary>Anchors reachable from this one with MoveWhileStanding (symmetric connections).</summary>
        public List<string> ConnectedAnchorIds { get; } = new List<string>();
    }

    /// <summary>
    /// A reusable transition binding. ApplicableAnchorIds empty means the
    /// operation is shared by every anchor whose capabilities satisfy its
    /// kind's precondition; a non-empty list records an exceptional anchor
    /// transition override without multiplying state-edge boilerplate.
    /// </summary>
    public sealed class PresentationOperationDefinition
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Kind { get; set; } = PresentationOperationKinds.MoveWhileStanding;
        public int Cost { get; set; } = 10;
        public List<string> ApplicableAnchorIds { get; } = new List<string>();
    }

    /// <summary>
    /// The generated, read-only projection of Unity ingredients and stage
    /// capabilities. It contains minimal semantic descriptors and stable
    /// ingredient IDs — never Unity objects, coordinates, bone names or a second
    /// game-content representation. WPF reads it for diagnostics; Core consumes
    /// it for planning. It is a versioned artifact, generated atomically, and a
    /// generation failure must remain visible rather than being reported as
    /// current.
    /// </summary>
    public sealed class PresentationCatalogDefinition
    {
        public const int CurrentVersion = 1;

        public int CatalogVersion { get; set; } = CurrentVersion;

        /// <summary>Informational generation timestamp (UTC, ISO-8601); never used for selection.</summary>
        public string GeneratedAtUtc { get; set; } = "";

        public List<PresentationIngredientDefinition> Ingredients { get; set; } = new List<PresentationIngredientDefinition>();
        public List<PresentationAnchorDefinition> Anchors { get; set; } = new List<PresentationAnchorDefinition>();
        public List<PresentationOperationDefinition> Operations { get; set; } = new List<PresentationOperationDefinition>();
    }

    /// <summary>
    /// Validates a presentation catalog before Core uses it. Fails loudly with
    /// the exact offender; an unresolved or malformed catalog is never silently
    /// treated as empty. Deliberately contains no database or Unity knowledge.
    /// </summary>
    public static class PresentationCatalogValidator
    {
        public static void Validate(PresentationCatalogDefinition catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (catalog.CatalogVersion != PresentationCatalogDefinition.CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"PresentationCatalog version {catalog.CatalogVersion} is not supported " +
                    $"(expected {PresentationCatalogDefinition.CurrentVersion}).");
            }

            var ingredientIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ingredient in catalog.Ingredients ?? new List<PresentationIngredientDefinition>())
            {
                if (ingredient == null)
                {
                    throw new InvalidOperationException("PresentationCatalog contains a null ingredient.");
                }
                RequireId(ingredient.Id, "ingredient");
                if (!ingredientIds.Add(ingredient.Id))
                {
                    throw new InvalidOperationException($"PresentationCatalog has duplicate ingredient id '{ingredient.Id}'.");
                }
                if (!PresentationIngredientKinds.IsKnown(ingredient.Kind))
                {
                    throw new InvalidOperationException(
                        $"Presentation ingredient '{ingredient.Id}' has unknown kind '{ingredient.Kind}'.");
                }
                foreach (var posture in ingredient.SupportedPostureIds)
                {
                    if (!PresentationPostures.IsKnown(posture))
                    {
                        throw new InvalidOperationException(
                            $"Presentation ingredient '{ingredient.Id}' declares unknown posture '{posture}'.");
                    }
                }
                if (!ingredient.Enabled) continue;

                // Enabled ingredient declarations must be complete enough to be selectable.
                if (ingredient.SupportedPostureIds.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Enabled presentation ingredient '{ingredient.Id}' declares no supported posture.");
                }
                if (ingredient.Kind != PresentationIngredientKinds.Foundation
                    && ingredient.PerformanceTagIds.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Enabled {ingredient.Kind} ingredient '{ingredient.Id}' has no Performance Tag; it can never be selected.");
                }
            }

            var anchorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var anchor in catalog.Anchors ?? new List<PresentationAnchorDefinition>())
            {
                if (anchor == null)
                {
                    throw new InvalidOperationException("PresentationCatalog contains a null anchor.");
                }
                RequireId(anchor.Id, "anchor");
                if (!anchorIds.Add(anchor.Id))
                {
                    throw new InvalidOperationException($"PresentationCatalog has duplicate anchor id '{anchor.Id}'.");
                }
                if (anchor.SupportedPostureIds.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Presentation anchor '{anchor.Id}' supports no posture.");
                }
                foreach (var posture in anchor.SupportedPostureIds)
                {
                    if (!PresentationPostures.IsKnown(posture))
                    {
                        throw new InvalidOperationException(
                            $"Presentation anchor '{anchor.Id}' declares unknown posture '{posture}'.");
                    }
                }
            }

            foreach (var anchor in catalog.Anchors ?? new List<PresentationAnchorDefinition>())
            {
                if (anchor == null) continue;
                foreach (var connected in anchor.ConnectedAnchorIds)
                {
                    if (!anchorIds.Contains(connected))
                    {
                        throw new InvalidOperationException(
                            $"Presentation anchor '{anchor.Id}' connects to unknown anchor '{connected}'.");
                    }
                }
            }

            foreach (var ingredient in catalog.Ingredients ?? new List<PresentationIngredientDefinition>())
            {
                if (ingredient == null) continue;
                foreach (var anchorId in ingredient.SupportedAnchorIds)
                {
                    if (!anchorIds.Contains(anchorId))
                    {
                        throw new InvalidOperationException(
                            $"Presentation ingredient '{ingredient.Id}' supports unknown anchor '{anchorId}'.");
                    }
                }
            }

            var operationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in catalog.Operations ?? new List<PresentationOperationDefinition>())
            {
                if (operation == null)
                {
                    throw new InvalidOperationException("PresentationCatalog contains a null operation.");
                }
                RequireId(operation.Id, "operation");
                if (!operationIds.Add(operation.Id))
                {
                    throw new InvalidOperationException($"PresentationCatalog has duplicate operation id '{operation.Id}'.");
                }
                if (!PresentationOperationKinds.IsKnown(operation.Kind))
                {
                    throw new InvalidOperationException(
                        $"Presentation operation '{operation.Id}' has unknown kind '{operation.Kind}'.");
                }
                if (operation.Cost <= 0)
                {
                    throw new InvalidOperationException(
                        $"Presentation operation '{operation.Id}' must have a positive cost.");
                }
                foreach (var anchorId in operation.ApplicableAnchorIds)
                {
                    if (!anchorIds.Contains(anchorId))
                    {
                        throw new InvalidOperationException(
                            $"Presentation operation '{operation.Id}' applies to unknown anchor '{anchorId}'.");
                    }
                }
            }
        }

        private static void RequireId(string id, string what)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException($"PresentationCatalog has a {what} with no id.");
            }
        }
    }
}
