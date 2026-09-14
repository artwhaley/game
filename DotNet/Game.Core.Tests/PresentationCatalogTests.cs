using System;
using System.Collections.Generic;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    [TestFixture]
    public class PresentationCatalogTests
    {
        [Test]
        public void V1Fixture_Validates()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            Assert.DoesNotThrow(() => PresentationCatalogValidator.Validate(catalog));
            Assert.AreEqual(6, catalog.Ingredients.Count);
            Assert.AreEqual(2, catalog.Anchors.Count);
            // One shared operation set serves both anchors: adding an equivalent
            // sit-capable anchor never copies posture conversions.
            Assert.AreEqual(3, catalog.Operations.Count);
        }

        [Test]
        public void RoundTrips_ThroughJson_PreservingSemanticContent()
        {
            var catalog = PerformanceTestCatalog.CreateV1();

            var json = PresentationCatalogJson.ToJson(catalog);
            var readBack = PresentationCatalogJson.FromJson(json);

            Assert.AreEqual(catalog.CatalogVersion, readBack.CatalogVersion);
            Assert.AreEqual(catalog.GeneratedAtUtc, readBack.GeneratedAtUtc);
            Assert.AreEqual(catalog.Ingredients.Count, readBack.Ingredients.Count);
            Assert.AreEqual(catalog.Anchors.Count, readBack.Anchors.Count);
            Assert.AreEqual(catalog.Operations.Count, readBack.Operations.Count);

            var smile = readBack.Ingredients.Find(i => i.Id == PerformanceTestCatalog.FaceSmile);
            Assert.IsNotNull(smile);
            Assert.AreEqual(PresentationIngredientKinds.Face, smile.Kind);
            CollectionAssert.AreEquivalent(
                new[] { PerformanceTestCatalog.TagPlayful, PerformanceTestCatalog.TagTease },
                smile.PerformanceTagIds);
            CollectionAssert.AreEquivalent(
                new[] { PresentationPostures.Standing, PresentationPostures.Sitting },
                smile.SupportedPostureIds);

            var chair = readBack.Anchors.Find(a => a.Id == PerformanceTestCatalog.AnchorChair);
            Assert.IsNotNull(chair);
            CollectionAssert.Contains(chair.ConnectedAnchorIds, PerformanceTestCatalog.AnchorRoomCenter);
            CollectionAssert.Contains(chair.SupportedPostureIds, PresentationPostures.Sitting);

            var sit = readBack.Operations.Find(o => o.Kind == PresentationOperationKinds.Sit);
            Assert.IsNotNull(sit);
            Assert.AreEqual(10, sit.Cost);

            // And the read-back catalog validates identically.
            Assert.DoesNotThrow(() => PresentationCatalogValidator.Validate(readBack));
        }

        [Test]
        public void Json_IsDeterministic_AndEscapesStrings()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Ingredients[0].DisplayName = "Quote \" and \\ slash\nline";

            var first = PresentationCatalogJson.ToJson(catalog);
            var second = PresentationCatalogJson.ToJson(catalog);
            Assert.AreEqual(first, second);

            var readBack = PresentationCatalogJson.FromJson(first);
            Assert.AreEqual("Quote \" and \\ slash\nline", readBack.Ingredients[0].DisplayName);
        }

        [Test]
        public void Json_RejectsMalformedContent_Loudly()
        {
            Assert.Throws<FormatException>(() => PresentationCatalogJson.FromJson("{ not json"));
            Assert.Throws<FormatException>(() => PresentationCatalogJson.FromJson("[1,2,3]"));
            Assert.Throws<FormatException>(() => PresentationCatalogJson.FromJson("{\"ingredients\": 5}"));
        }

        [Test]
        public void Validator_RejectsUnsupportedVersion()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.CatalogVersion = 999;
            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogValidator.Validate(catalog));
            StringAssert.Contains("version", failure.Message);
        }

        [Test]
        public void Validator_RejectsDuplicateIngredientIds()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Ingredients.Add(catalog.Ingredients[0]);
            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogValidator.Validate(catalog));
            StringAssert.Contains("duplicate ingredient id", failure.Message);
        }

        [Test]
        public void Validator_RejectsEnabledExpressiveIngredientWithoutTags()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Ingredients.Add(new PresentationIngredientDefinition
            {
                Id = "ing-body-untagged",
                Kind = PresentationIngredientKinds.Body,
                Enabled = true,
            });
            catalog.Ingredients[catalog.Ingredients.Count - 1]
                .SupportedPostureIds.Add(PresentationPostures.Standing);

            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogValidator.Validate(catalog));
            StringAssert.Contains("no Performance Tag", failure.Message);
        }

        [Test]
        public void Validator_AcceptsDisabledIncompleteIngredient()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Ingredients.Add(new PresentationIngredientDefinition
            {
                Id = "ing-body-wip",
                Kind = PresentationIngredientKinds.Body,
                Enabled = false,
            });

            Assert.DoesNotThrow(() => PresentationCatalogValidator.Validate(catalog));
        }

        [Test]
        public void Validator_RejectsUnknownOperationKind()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Operations[0].Kind = "teleport";
            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogValidator.Validate(catalog));
            StringAssert.Contains("unknown kind", failure.Message);
        }

        [Test]
        public void Validator_RejectsNonPositiveOperationCost()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Operations[0].Cost = 0;
            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogValidator.Validate(catalog));
            StringAssert.Contains("positive cost", failure.Message);
        }

        [Test]
        public void Validator_RejectsDanglingAnchorConnection()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Anchors[0].ConnectedAnchorIds.Add("anchor-nowhere");
            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogValidator.Validate(catalog));
            StringAssert.Contains("unknown anchor", failure.Message);
        }

        [Test]
        public void Validator_RejectsUnknownPosture()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Anchors[0].SupportedPostureIds.Add("floating");
            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogValidator.Validate(catalog));
            StringAssert.Contains("unknown posture", failure.Message);
        }

        [Test]
        public void Validator_RejectsIngredientSupportingUnknownAnchor()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Ingredients[2].SupportedAnchorIds.Add("anchor-nowhere");
            var failure = Assert.Throws<InvalidOperationException>(
                () => PresentationCatalogValidator.Validate(catalog));
            StringAssert.Contains("unknown anchor", failure.Message);
        }

        [Test]
        public void FactoredOperations_AreSharedAcrossCompatibleAnchors()
        {
            // Adding an equivalent sit-capable anchor must reuse the same
            // operation definitions rather than duplicating posture conversions.
            var catalog = PerformanceTestCatalog.CreateV1();
            PerformanceTestCatalog.AddAnchor(catalog, "anchor-chair-b", "Chair B", "chair-b",
                new[] { PresentationPostures.Standing, PresentationPostures.Sitting },
                PerformanceTestCatalog.AnchorRoomCenter);

            Assert.DoesNotThrow(() => PresentationCatalogValidator.Validate(catalog));
            Assert.AreEqual(3, catalog.Operations.Count,
                "a new compatible anchor must not require new operations");
            foreach (var operation in catalog.Operations)
            {
                Assert.AreEqual(0, operation.ApplicableAnchorIds.Count,
                    "shared operations stay unrestricted so every compatible anchor reuses them");
            }
        }
    }
}
