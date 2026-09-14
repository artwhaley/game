using System;
using System.Collections.Generic;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Performance;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// Ticket 01: the Unity registry's read-only projection must be exactly the
    /// portable PresentationCatalog contract Core and WPF consume. These tests
    /// use plain entry objects (no imported assets) so they run headlessly.
    /// </summary>
    [TestFixture]
    public class PerformanceRegistryTests
    {
        private static readonly string[] Standing = { PresentationPostures.Standing };
        private static readonly string[] BothPostures =
            { PresentationPostures.Standing, PresentationPostures.Sitting };

        [Test]
        public void RegistryView_ProjectsAValidCatalog()
        {
            var catalog = BuildV1();

            Assert.DoesNotThrow(() => PresentationCatalogValidator.Validate(catalog));
            Assert.AreEqual(6, catalog.Ingredients.Count);
            Assert.AreEqual(2, catalog.Anchors.Count);
            Assert.AreEqual(3, catalog.Operations.Count);
        }

        [Test]
        public void Catalog_RoundTripsThroughJson()
        {
            var catalog = BuildV1();
            var json = PresentationCatalogBuilder.ToJson(catalog);
            var readBack = PresentationCatalogJson.FromJson(json);

            Assert.DoesNotThrow(() => PresentationCatalogValidator.Validate(readBack));
            Assert.AreEqual(catalog.Ingredients.Count, readBack.Ingredients.Count);
            CollectionAssert.AreEquivalent(
                new[] { PerformanceCatalogSetupTags.Playful, PerformanceCatalogSetupTags.Tease },
                readBack.Ingredients.Find(i => i.Id == "ing-smile").PerformanceTagIds);
        }

        [Test]
        public void DisabledIncompleteIngredient_IsAllowed()
        {
            var view = new PerformanceRegistryView(null,
                new List<PerformanceIngredientEntry>
                {
                    Body("ing-body-wip", enabled: false, tags: Array.Empty<string>()),
                },
                new List<PerformanceAnchorEntry>(),
                new List<PerformanceOperationEntry>());

            Assert.DoesNotThrow(() => PresentationCatalogBuilder.BuildValidated(view, DateTime.UtcNow));
        }

        [Test]
        public void EnabledExpressiveIngredientWithoutTags_FailsLoudly()
        {
            var view = new PerformanceRegistryView(null,
                new List<PerformanceIngredientEntry>
                {
                    Body("ing-body-untagged", enabled: true, tags: Array.Empty<string>()),
                },
                new List<PerformanceAnchorEntry>(),
                new List<PerformanceOperationEntry>());

            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogBuilder.BuildValidated(view, DateTime.UtcNow));
            StringAssert.Contains("no Performance Tag", failure.Message);
        }

        [Test]
        public void DuplicateIngredientIds_AreRejected()
        {
            var view = new PerformanceRegistryView(null,
                new List<PerformanceIngredientEntry>
                {
                    Body("ing-dup", enabled: true, tags: new[] { PerformanceCatalogSetupTags.Playful }),
                    Body("ing-dup", enabled: true, tags: new[] { PerformanceCatalogSetupTags.Playful }),
                },
                new List<PerformanceAnchorEntry>(),
                new List<PerformanceOperationEntry>());

            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogBuilder.BuildValidated(view, DateTime.UtcNow));
            StringAssert.Contains("duplicate ingredient id", failure.Message);
        }

        [Test]
        public void SecondEquivalentAnchor_ReusesSharedOperations()
        {
            var entries = new List<PerformanceIngredientEntry>
            {
                Foundation("ing-standing", Standing),
                Foundation("ing-sitting", new[] { PresentationPostures.Sitting }),
            };
            var anchors = new List<PerformanceAnchorEntry>
            {
                Anchor("anchor-room", Standing, "anchor-chair-a", "anchor-chair-b"),
                Anchor("anchor-chair-a", BothPostures, "anchor-room"),
                Anchor("anchor-chair-b", BothPostures, "anchor-room"),
            };
            var operations = SharedOperations();

            var view = new PerformanceRegistryView(null, entries, anchors, operations);
            var catalog = PresentationCatalogBuilder.BuildValidated(view, DateTime.UtcNow);

            Assert.AreEqual(3, catalog.Operations.Count);
            foreach (var operation in catalog.Operations)
            {
                Assert.AreEqual(0, operation.ApplicableAnchorIds.Count,
                    "shared operations apply to every compatible anchor");
            }
        }

        private static PresentationCatalogDefinition BuildV1()
        {
            var entries = new List<PerformanceIngredientEntry>
            {
                Foundation("ing-standing", Standing),
                Foundation("ing-sitting", new[] { PresentationPostures.Sitting }),
                Body("ing-talking", true, new[] { PerformanceCatalogSetupTags.Playful, PerformanceCatalogSetupTags.Tease }, BothPostures),
                Body("ing-interact", true, new[] { PerformanceCatalogSetupTags.Tease }, Standing),
                Face("ing-smile", new[] { PerformanceCatalogSetupTags.Playful, PerformanceCatalogSetupTags.Tease }),
                Face("ing-frown", new[] { PerformanceCatalogSetupTags.Stern }),
            };
            var anchors = new List<PerformanceAnchorEntry>
            {
                Anchor("anchor-room", Standing, "anchor-chair"),
                Anchor("anchor-chair", BothPostures, "anchor-room"),
            };
            var view = new PerformanceRegistryView(null, entries, anchors, SharedOperations());
            return PresentationCatalogBuilder.BuildValidated(view, DateTime.UtcNow);
        }

        private static List<PerformanceOperationEntry> SharedOperations()
        {
            return new List<PerformanceOperationEntry>
            {
                Operation("op-stand", PresentationOperationKinds.Stand),
                Operation("op-sit", PresentationOperationKinds.Sit),
                Operation("op-move", PresentationOperationKinds.MoveWhileStanding),
            };
        }

        private static PerformanceIngredientEntry Foundation(string id, string[] postures)
        {
            return Ingredient(id, PresentationIngredientKinds.Foundation, true, Array.Empty<string>(), postures);
        }

        private static PerformanceIngredientEntry Body(string id, bool enabled, string[] tags,
            string[] postures = null)
        {
            return Ingredient(id, PresentationIngredientKinds.Body, enabled, tags, postures ?? Standing);
        }

        private static PerformanceIngredientEntry Face(string id, string[] tags)
        {
            return Ingredient(id, PresentationIngredientKinds.Face, true, tags, BothPostures);
        }

        private static PerformanceIngredientEntry Ingredient(
            string id, string kind, bool enabled, string[] tags, string[] postures)
        {
            var entry = new PerformanceIngredientEntry();
            entry.Configure(id, id, kind, enabled, tags, postures, false, null);
            return entry;
        }

        private static PerformanceAnchorEntry Anchor(string id, string[] postures, params string[] connections)
        {
            var entry = new PerformanceAnchorEntry();
            entry.Configure(id, id, id, postures, connections);
            return entry;
        }

        private static PerformanceOperationEntry Operation(string id, string kind)
        {
            var entry = new PerformanceOperationEntry();
            entry.Configure(id, kind, kind, 10);
            return entry;
        }

        /// <summary>Mirror of the editor fixture's tag vocabulary without referencing editor code.</summary>
        private static class PerformanceCatalogSetupTags
        {
            public const string Playful = "perf-tag-playful";
            public const string Tease = "perf-tag-tease";
            public const string Stern = "perf-tag-stern";
        }
    }
}
