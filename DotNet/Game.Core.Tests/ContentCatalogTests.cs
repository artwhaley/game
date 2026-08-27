using System;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    [TestFixture]
    public class ContentCatalogTests
    {
        private static ContentCatalog Catalog() => new ContentCatalog(SampleContent.Create());

        [Test]
        public void Resolves_EveryV2EntityKind_ByStableId()
        {
            var catalog = Catalog();

            Assert.AreEqual("Intense", catalog.SessionById(SampleContent.SessionIntense).Title);
            Assert.AreEqual("Warm Up", catalog.PhaseById(SampleContent.PhaseWarmUp).Title);
            Assert.AreEqual("The End", catalog.CardById(SampleContent.CardTheEnd).Title);
            Assert.AreEqual("A Familiar Face", catalog.ResourceById(SampleContent.ResourceCutsceneIntro).Name);
            Assert.AreEqual("Standard", catalog.SessionTypeById(SampleContent.TypeStandard).Title);
            Assert.AreEqual(50f, catalog.TemperatureById(SampleContent.TemperatureHappiness).DefaultValue);
        }

        [Test]
        public void MissingLookups_FailLoudly_WithContext()
        {
            var catalog = Catalog();

            var missing = Assert.Throws<InvalidOperationException>(() => catalog.PhaseById("does-not-exist"));
            StringAssert.Contains("Phase", missing.Message);
            StringAssert.Contains("does-not-exist", missing.Message);
        }

        [Test]
        public void DuplicateIds_AreRejected_AtConstruction()
        {
            var content = SampleContent.Create();
            content.Cards.Add(content.Cards[0]); // same instance object twice => duplicate id

            var failure = Assert.Throws<InvalidOperationException>(() => _ = new ContentCatalog(content));
            StringAssert.Contains("duplicate", failure.Message);
        }

        [Test]
        public void EmptyIds_AreRejected_AtConstruction()
        {
            var content = SampleContent.Create();
            content.Phases.Add(new PhaseDefinition { Title = "No Id" });

            var failure = Assert.Throws<InvalidOperationException>(() => _ = new ContentCatalog(content));
            StringAssert.Contains("missing/empty id", failure.Message);
        }

        [Test]
        public void TryLookup_ExistsForOptionalReferenceSeams()
        {
            var catalog = Catalog();

            Assert.That(catalog.TrySessionById(SampleContent.SessionRelaxing, out var relaxing), Is.True);
            Assert.AreEqual("Relaxing", relaxing.Title);
            Assert.That(catalog.TrySessionById("nope", out _), Is.False);
        }

        [Test]
        public void SessionReferences_ExactlyOneSessionType()
        {
            var content = SampleContent.Create();
            foreach (var session in content.Sessions)
            {
                Assert.AreEqual(SampleContent.TypeStandard, session.SessionTypeId,
                    "session '" + session.Title + "' must carry its type reference");
                // And the catalog resolves it:
                Assert.IsNotNull(new ContentCatalog(content).SessionTypeById(session.SessionTypeId));
            }
        }
    }
}
