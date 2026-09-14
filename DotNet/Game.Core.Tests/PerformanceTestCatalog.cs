using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Builds an in-memory V1 presentation catalog matching the Unity fixture's
    /// shape (standing room-center + sit-capable chair, a shared stand/sit/move
    /// operation set, foundation/body/face ingredients). Used by the catalog
    /// codec/validator tests and the Ticket 02 planner tests; the real catalog is
    /// generated from the Unity registry.
    /// </summary>
    public static class PerformanceTestCatalog
    {
        public const string TagPlayful = "perf-tag-playful";
        public const string TagTease = "perf-tag-tease";
        public const string TagStern = "perf-tag-stern";

        public const string AnchorRoomCenter = "anchor-room-center";
        public const string AnchorChair = "anchor-chair";

        public const string FoundationStanding = "ing-foundation-standing";
        public const string FoundationSitting = "ing-foundation-sitting";
        public const string BodyTalking = "ing-body-talking";
        public const string BodyInteract = "ing-body-interact";
        public const string FaceSmile = "ing-face-smile";
        public const string FaceFrown = "ing-face-frown";

        public static PresentationCatalogDefinition CreateV1()
        {
            var catalog = new PresentationCatalogDefinition
            {
                CatalogVersion = PresentationCatalogDefinition.CurrentVersion,
                GeneratedAtUtc = "2026-09-13T00:00:00.0000000Z",
            };

            AddIngredient(catalog, FoundationStanding, "Standing Idle",
                PresentationIngredientKinds.Foundation, new[] { PresentationPostures.Standing });
            AddIngredient(catalog, FoundationSitting, "Sitting Idle",
                PresentationIngredientKinds.Foundation, new[] { PresentationPostures.Sitting });
            AddIngredient(catalog, BodyTalking, "Talking Idle",
                PresentationIngredientKinds.Body,
                new[] { PresentationPostures.Standing, PresentationPostures.Sitting },
                TagPlayful, TagTease);
            AddIngredient(catalog, BodyInteract, "Interact",
                PresentationIngredientKinds.Body, new[] { PresentationPostures.Standing }, TagTease);
            AddIngredient(catalog, FaceSmile, "Smile",
                PresentationIngredientKinds.Face,
                new[] { PresentationPostures.Standing, PresentationPostures.Sitting },
                TagPlayful, TagTease);
            AddIngredient(catalog, FaceFrown, "Frown",
                PresentationIngredientKinds.Face,
                new[] { PresentationPostures.Standing, PresentationPostures.Sitting }, TagStern);

            AddAnchor(catalog, AnchorRoomCenter, "Room Center", "center",
                new[] { PresentationPostures.Standing }, AnchorChair);
            AddAnchor(catalog, AnchorChair, "Chair", "chair",
                new[] { PresentationPostures.Standing, PresentationPostures.Sitting }, AnchorRoomCenter);

            AddOperation(catalog, "op-stand", "Stand", PresentationOperationKinds.Stand);
            AddOperation(catalog, "op-sit", "Sit", PresentationOperationKinds.Sit);
            AddOperation(catalog, "op-move-standing", "Move While Standing",
                PresentationOperationKinds.MoveWhileStanding);

            return catalog;
        }

        public static PresentationIngredientDefinition AddIngredient(
            PresentationCatalogDefinition catalog, string id, string name, string kind,
            IEnumerable<string> postures, params string[] tags)
        {
            var ingredient = new PresentationIngredientDefinition
            {
                Id = id,
                DisplayName = name,
                Kind = kind,
                Enabled = true,
            };
            ingredient.SupportedPostureIds.AddRange(postures);
            ingredient.PerformanceTagIds.AddRange(tags);
            catalog.Ingredients.Add(ingredient);
            return ingredient;
        }

        public static PresentationAnchorDefinition AddAnchor(
            PresentationCatalogDefinition catalog, string id, string name, string locationGroup,
            IEnumerable<string> postures, params string[] connections)
        {
            var anchor = new PresentationAnchorDefinition
            {
                Id = id,
                DisplayName = name,
                LocationGroup = locationGroup,
            };
            anchor.SupportedPostureIds.AddRange(postures);
            anchor.ConnectedAnchorIds.AddRange(connections);
            catalog.Anchors.Add(anchor);
            return anchor;
        }

        public static PresentationOperationDefinition AddOperation(
            PresentationCatalogDefinition catalog, string id, string name, string kind, int cost = 10)
        {
            var operation = new PresentationOperationDefinition
            {
                Id = id,
                DisplayName = name,
                Kind = kind,
                Cost = cost,
            };
            catalog.Operations.Add(operation);
            return operation;
        }
    }
}
