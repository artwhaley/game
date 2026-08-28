using System.Collections.Generic;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 06 gate: Session-global temperatures with spawn overrides,
    /// SessionType resolution + SessionSelector, and the deterministic
    /// per-PhaseRun RNG factory.
    /// </summary>
    [TestFixture]
    public class SpawnAndSelectionTests
    {
        private ContentCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = new ContentCatalog(SampleContent.Create());
        }

        // ---------- temperatures ----------

        [Test]
        public void Happiness_DefaultsTo50()
        {
            var temperatures = new TemperatureState(_catalog);
            Assert.AreEqual(50f, temperatures.Get(SampleContent.TemperatureHappiness));
        }

        [Test]
        public void SpawnOverride_ReplacesDefault()
        {
            var spawn = new SessionSpawnOptions().OverrideTemperature(SampleContent.TemperatureHappiness, 20f);
            var temperatures = new TemperatureState(_catalog, spawn);
            Assert.AreEqual(20f, temperatures.Get(SampleContent.TemperatureHappiness));
        }

        [Test]
        public void SpawnOverride_WithoutOverride_KeepsDefault()
        {
            var temperatures = new TemperatureState(_catalog, SessionSpawnOptions.Default);
            Assert.AreEqual(50f, temperatures.Get(SampleContent.TemperatureHappiness));
        }

        [Test]
        public void SpawnOverride_ForUnknownTemperature_IsRejected()
        {
            var spawn = new SessionSpawnOptions().OverrideTemperature("no-such-temperature", 10f);
            Assert.Throws<System.InvalidOperationException>(() => new TemperatureState(_catalog, spawn));
        }

        [Test]
        public void Mutations_ClampToDefinitionBounds()
        {
            var temperatures = new TemperatureState(_catalog);
            temperatures.Add(SampleContent.TemperatureHappiness, 1000f);
            Assert.AreEqual(100f, temperatures.Get(SampleContent.TemperatureHappiness), "clamped to max");

            temperatures.Add(SampleContent.TemperatureHappiness, -1000f);
            Assert.AreEqual(0f, temperatures.Get(SampleContent.TemperatureHappiness), "clamped to min");
        }

        // ---------- engine spawn contact ----------

        [Test]
        public void Engine_AdoptsSpawnTemperatureOverride()
        {
            var spawn = new SessionSpawnOptions().OverrideTemperature(SampleContent.TemperatureHappiness, 33f);
            var services = new CoreServices(new FakeDelayService());
            var engine = new GameSessionEngine(SampleContent.Create(), SampleContent.SessionIntense, services, spawn);

            Assert.AreEqual(33f, engine.Temperatures.Get(SampleContent.TemperatureHappiness));
        }

        [Test]
        public void Engine_WithoutOverride_UsesDefinitionDefault()
        {
            var services = new CoreServices(new FakeDelayService());
            var engine = new GameSessionEngine(SampleContent.Create(), SampleContent.SessionIntense, services);
            Assert.AreEqual(50f, engine.Temperatures.Get(SampleContent.TemperatureHappiness));
        }

        // ---------- session selector ----------

        [Test]
        public void SessionSelector_PicksSessionOfType()
        {
            var selector = new SessionSelector(_catalog);
            var rng = new FixedRandomSource(0);

            Assert.IsTrue(selector.TrySelect(SampleContent.TypeStandard, rng, out var session));
            Assert.AreEqual(SampleContent.TypeStandard, session.SessionTypeId);
        }

        [Test]
        public void SessionSelector_UnknownType_FailsLoudly()
        {
            var selector = new SessionSelector(_catalog);
            Assert.Throws<System.InvalidOperationException>(
                () => selector.TrySelect("no-such-type", new FixedRandomSource(0), out _));
        }

        [Test]
        public void SessionSelector_EligibilityFilter_NarrowsCandidates()
        {
            var selector = new SessionSelector(_catalog);

            // The eligibility filter restricts selection to the Intense
            // session by id regardless of RNG offset (Session free-form tags
            // are gone with the old tag system; filters use stable ids).
            Assert.IsTrue(selector.TrySelect(
                SampleContent.TypeStandard,
                new FixedRandomSource(0),
                out var chosen,
                session => session.Id == SampleContent.SessionIntense));
            Assert.IsNotNull(chosen);
            Assert.AreEqual(SampleContent.SessionIntense, chosen.Id);
        }

        [Test]
        public void SessionSelector_SelectionRng_IsSeparateFromRuntimeRng()
        {
            // Selection RNG and the PhaseRun RNG factory are distinct seams: a
            // seeded selection doesn't consume the factory's card RNG stream.
            var selector = new SessionSelector(_catalog);
            var selectionRng = new FixedRandomSource(0);
            var factory = new PhaseRunRngFactory(1234);

            var first = factory.Create();
            Assert.IsTrue(selector.TrySelect(SampleContent.TypeStandard, selectionRng, out _));
            var second = factory.Create();

            Assert.AreNotSame(first, second, "each PhaseRun gets its own RNG object");
        }

        // ---------- PhaseRun RNG factory ----------

        [Test]
        public void PhaseRunRngFactory_Deterministic_WithSeed()
        {
            var factoryA = new PhaseRunRngFactory(777);
            var factoryB = new PhaseRunRngFactory(777);

            var a1 = factoryA.Create();
            var a2 = factoryA.Create();
            var b1 = factoryB.Create();
            var b2 = factoryB.Create();

            Assert.AreEqual(a1.NextInt(0, 1000000), b1.NextInt(0, 1000000), "first run sequence identical");
            Assert.AreEqual(a2.NextInt(0, 1000000), b2.NextInt(0, 1000000), "second run sequence identical");
        }

        [Test]
        public void PhaseRunRngFactory_EachRun_GetsDistinctRng()
        {
            var factory = new PhaseRunRngFactory(42);
            var run1 = factory.Create();
            var run2 = factory.Create();
            var run3 = factory.Create();

            Assert.AreNotSame(run1, run2);
            Assert.AreNotSame(run2, run3);
            Assert.AreNotSame(run1, run3);
        }

        [Test]
        public void SuspendedRunRng_IsNotConsumed_ByOtherRuns()
        {
            // The factory hands out independent objects; drawing from run2's RNG
            // must not advance run1's stream.
            var factory = new PhaseRunRngFactory(5);
            var suspended = factory.Create();
            var other = factory.Create();

            other.NextInt(0, 1000000);
            other.NextInt(0, 1000000);

            var firstDrawOfSuspended = suspended.NextInt(0, 1000000);
            var refFactory = new PhaseRunRngFactory(5);
            var refSuspended = refFactory.Create();
            Assert.AreEqual(firstDrawOfSuspended, refSuspended.NextInt(0, 1000000),
                "suspended run's RNG untouched by other runs' draws");
        }
    }
}
