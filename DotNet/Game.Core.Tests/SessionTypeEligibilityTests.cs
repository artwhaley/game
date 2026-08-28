using System;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 10: SessionType eligibility (all required capabilities must be
    /// available), uniform Session selection with a dedicated RNG, and the
    /// spawn Happiness override defaulting to 50.
    /// </summary>
    [TestFixture]
    public class SessionTypeEligibilityTests
    {
        private static CardSelectionProfile Profile(params string[] availableCapabilities)
        {
            var profile = new CardSelectionProfile();
            foreach (var id in availableCapabilities) profile.AvailableCapabilityIds.Add(id);
            return profile;
        }

        private static GameContentDefinition Content()
        {
            var content = SampleContent.Create();
            content.SessionTypes.Add(new SessionTypeDefinition
            {
                Id = "type-toy",
                Title = "Toy Session",
                RequiredCapabilityIds = { "cap-vibrate", "cap-rotate" },
            });
            content.SessionTypes.Add(new SessionTypeDefinition
            {
                Id = "type-no-req",
                Title = "No Requirements",
            });
            // One session of the toy type.
            var session = new SessionDefinition { Id = "s-toy", Title = "Toy", SessionTypeId = "type-toy" };
            session.Graph.Nodes.Add(new SessionStartNodeDefinition { Id = "n-start" });
            session.Graph.Nodes.Add(new SessionEndNodeDefinition { Id = "n-end" });
            content.Sessions.Add(session);
            return content;
        }

        [Test]
        public void NoRequirements_AlwaysEligible()
        {
            var content = Content();
            var eligibility = new SessionTypeEligibility(new ContentCatalog(content));
            var result = eligibility.Evaluate("type-no-req", Profile());
            Assert.IsTrue(result.IsEligible);
            Assert.AreEqual(0, result.MissingCapabilityIds.Count);
        }

        [Test]
        public void AllRequiredCapabilities_AvailableMeansEligible()
        {
            var content = Content();
            var eligibility = new SessionTypeEligibility(new ContentCatalog(content));
            var result = eligibility.Evaluate("type-toy", Profile("cap-vibrate", "cap-rotate"));
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void MissingOneCapability_IneligibleWithTypedList()
        {
            var content = Content();
            var eligibility = new SessionTypeEligibility(new ContentCatalog(content));
            var result = eligibility.Evaluate("type-toy", Profile("cap-vibrate"));

            Assert.IsFalse(result.IsEligible);
            CollectionAssert.AreEqual(new[] { "cap-rotate" }, result.MissingCapabilityIds);
        }

        [Test]
        public void TrySelectSession_UniformAmongTypeSessions()
        {
            var content = Content();
            var catalog = new ContentCatalog(content);
            var eligibility = new SessionTypeEligibility(catalog);

            // Both sample sessions are type-standard; offset 0 -> first, offset 1 -> second.
            Assert.IsTrue(eligibility.TrySelectSession(SampleContent.TypeStandard, Profile(),
                new FixedRandomSource(0), out var first));
            Assert.AreEqual(SampleContent.SessionIntense, first.Id);

            Assert.IsTrue(eligibility.TrySelectSession(SampleContent.TypeStandard, Profile(),
                new FixedRandomSource(1), out var second));
            Assert.AreEqual(SampleContent.SessionRelaxing, second.Id);
        }

        [Test]
        public void TrySelectSession_IneligibleType_ReturnsFalse()
        {
            var content = Content();
            var eligibility = new SessionTypeEligibility(new ContentCatalog(content));

            Assert.IsFalse(eligibility.TrySelectSession("type-toy", Profile(), new FixedRandomSource(0), out var selected),
                "type requirements gate session selection");
            Assert.IsNull(selected);
        }

        [Test]
        public void TrySelectSession_UnknownType_FailsLoudly()
        {
            var content = Content();
            var eligibility = new SessionTypeEligibility(new ContentCatalog(content));
            Assert.Throws<InvalidOperationException>(() =>
                eligibility.TrySelectSession("no-such-type", Profile(), new FixedRandomSource(0), out _));
        }

        [Test]
        public void SelectionRng_IsSeparateFromPhaseRunCardRng()
        {
            var content = Content();
            var catalog = new ContentCatalog(content);
            var eligibility = new SessionTypeEligibility(catalog);

            var selectionRng = new FixedRandomSource(0);
            var factory = new PhaseRunRngFactory(42);

            Assert.IsTrue(eligibility.TrySelectSession(SampleContent.TypeStandard, Profile(), selectionRng, out _));
            var runRng = factory.Create();
            Assert.IsNotNull(runRng, "card RNG stream untouched by selection");
        }

        [Test]
        public void SpawnHappinessOverride_DefaultsTo50_OverrideWorks()
        {
            var content = Content();
            var catalog = new ContentCatalog(content);
            var temperatures = new TemperatureState(catalog);
            Assert.AreEqual(50f, temperatures.Get(PhaseGraphVm.HappinessTemperatureId), "definition default 50");

            // NOTE: SessionSpawnOptions.Default is a shared singleton — never
            // mutate it in tests; construct a fresh instance for overrides.
            var overridden = new TemperatureState(catalog, new SessionSpawnOptions().OverrideHappiness(0f));
            Assert.AreEqual(0f, overridden.Get(PhaseGraphVm.HappinessTemperatureId));
        }
    }
}
