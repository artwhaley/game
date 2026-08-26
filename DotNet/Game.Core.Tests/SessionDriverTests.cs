using System;
using System.Linq;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core.Tests;

namespace TruthCardGame.Core
{
    public class SessionDriverTests
    {
        private sealed class MutableModifier
        {
            public float Value = 1f;
        }

        private static PhaseDefinition MakePhase(int min, int max, params string[] includeTags)
        {
            var phase = new PhaseDefinition
            {
                Title = "P" + Guid.NewGuid().ToString("N").Substring(0, 4),
                MinCards = min,
                MaxCards = max
            };
            phase.MustIncludeTags.AddRange(includeTags);
            return phase;
        }

        private static SessionDefinition MakeSession(params PhaseDefinition[] phases)
        {
            var session = new SessionDefinition { Title = "S" };
            session.Phases.AddRange(phases);
            return session;
        }

        [Test]
        public void Constructor_FirstPhase_IsCurrent_WithItsFilter()
        {
            var session = MakeSession(MakePhase(2, 2, "truth"), MakePhase(3, 3, "dare"));
            var driver = new SessionDriver(session, () => 1f, null, new FixedRandomSource(0));

            Assert.IsFalse(driver.IsComplete);
            Assert.AreEqual(0, driver.PhaseIndex);
            CollectionAssert.AreEqual(new[] { "truth" }, driver.CurrentMustInclude);
        }

        [Test]
        public void Phase_Advances_AfterScaledTargetDraws()
        {
            // P1: min=max=2 → base target 2. Modifier 1 → advance after 2 draws.
            var session = MakeSession(MakePhase(2, 2, "truth"), MakePhase(1, 1, "dare"));
            var driver = new SessionDriver(session, () => 1f, null, new FixedRandomSource(0, 0));

            driver.OnCardCompleted();
            Assert.AreEqual(0, driver.PhaseIndex);

            driver.OnCardCompleted();
            Assert.AreEqual(1, driver.PhaseIndex);
            CollectionAssert.AreEqual(new[] { "dare" }, driver.CurrentMustInclude);
            Assert.AreEqual(1, driver.CurrentTarget());
        }

        [Test]
        public void OnePhase_Session_CompletesAfterTarget()
        {
            var session = MakeSession(MakePhase(2, 2));
            var driver = new SessionDriver(session, () => 1f, null, new FixedRandomSource(0));

            driver.OnCardCompleted();
            Assert.IsFalse(driver.IsComplete);
            driver.OnCardCompleted();
            Assert.IsTrue(driver.IsComplete);
            Assert.AreEqual(-1, driver.PhaseIndex);
            CollectionAssert.IsEmpty(driver.CurrentMustInclude);
        }

        [Test]
        public void MinCards_BelowOne_ClampsToOne()
        {
            var session = MakeSession(MakePhase(-3, 0));
            var driver = new SessionDriver(session, () => 1f, null, new FixedRandomSource(0));

            Assert.AreEqual(1, driver.CurrentTarget());
            driver.OnCardCompleted();
            Assert.IsTrue(driver.IsComplete);
        }

        [Test]
        public void MaxCards_BelowMin_NormalizesToMin()
        {
            // min 5, max 2 → effective range collapses to [5,5]; base target must be 5.
            var session = MakeSession(MakePhase(5, 2));
            var driver = new SessionDriver(session, () => 1f, null, new FixedRandomSource(0));

            Assert.AreEqual(5, driver.CurrentTarget());
            driver.OnCardCompleted();
            Assert.AreEqual(4, driver.Remaining());
        }

        [Test]
        public void BaseTarget_UsesFullInclusiveRange()
        {
            // Range [2,4] (min 2 max 4): fixed source proves both endpoints reachable.
            var lower = new SessionDriver(MakeSession(MakePhase(2, 4)), () => 1f, null, new FixedRandomSource(0));
            var upper = new SessionDriver(MakeSession(MakePhase(2, 4)), () => 1f, null, new FixedRandomSource(2));

            Assert.AreEqual(2, lower.CurrentTarget());
            Assert.AreEqual(4, upper.CurrentTarget());
        }

        [Test]
        public void LiveModifier_GrowsPhase_WhenIncreasedMidPhase()
        {
            var mod = new MutableModifier();
            var session = MakeSession(MakePhase(3, 3));
            var driver = new SessionDriver(session, () => mod.Value, null, new FixedRandomSource(0));

            driver.OnCardCompleted();
            driver.OnCardCompleted();
            mod.Value = 2f;
            driver.OnCardCompleted();
            Assert.AreEqual(0, driver.PhaseIndex);
            Assert.AreEqual(3, driver.Remaining());
        }

        [Test]
        public void LiveModifier_ShrinksPhase_AndAdvancesImmediately()
        {
            var mod = new MutableModifier();
            var session = MakeSession(MakePhase(4, 4), MakePhase(1, 1));
            var driver = new SessionDriver(session, () => mod.Value, null, new FixedRandomSource(0, 0));

            driver.OnCardCompleted();
            driver.OnCardCompleted();
            mod.Value = 0.5f;
            driver.OnCardCompleted();
            Assert.AreEqual(1, driver.PhaseIndex);
        }

        [TestCase(1, 0.5f, 0, 1)]   // 0.5 → rounds to 0 → clamped to 1
        [TestCase(3, 0.5f, 0, 2)]   // 1.5 → midpoint-to-even → 2
        [TestCase(5, 0.5f, 0, 2)]   // 2.5 → midpoint-to-even → 2
        [TestCase(7, 0.5f, 0, 4)]   // 3.5 → midpoint-to-even → 4
        [TestCase(1, 1.5f, 0, 2)]   // 1.5 again via non-half base × half modifier
        public void CurrentTarget_MidpointRounding_IsToEven_ThenClamped(int baseTarget, float modifier, int rngValue, int expected)
        {
            var session = MakeSession(MakePhase(baseTarget, baseTarget));
            var driver = new SessionDriver(session, () => modifier, null, new FixedRandomSource(rngValue));
            Assert.AreEqual(expected, driver.CurrentTarget());
        }

        [Test]
        public void NoMatchingCard_Warns_AndAdvancesEarly()
        {
            var log = new RecordingLog();
            var session = MakeSession(MakePhase(5, 5), MakePhase(1, 1));
            var driver = new SessionDriver(session, () => 1f, log, new FixedRandomSource(0, 0));

            driver.OnNoMatchingCard();

            Assert.AreEqual(1, driver.PhaseIndex);
            Assert.That(log.Entries.Single(e => e.StartsWith("warn:")), Does.Contain("advancing early"));
        }

        [Test]
        public void NoMatch_OnFinalPhase_CompletesSession()
        {
            var session = MakeSession(MakePhase(1, 1));
            var driver = new SessionDriver(session, () => 1f, null, new FixedRandomSource(0));

            driver.OnNoMatchingCard();

            Assert.IsTrue(driver.IsComplete);
        }

        [Test]
        public void LastPhase_Completion_EndsSession()
        {
            var session = MakeSession(MakePhase(2, 2), MakePhase(3, 3));
            var driver = new SessionDriver(session, () => 1f, null, new FixedRandomSource(0, 0));

            driver.OnCardCompleted();
            driver.OnCardCompleted();
            Assert.AreEqual(1, driver.PhaseIndex);

            driver.OnCardCompleted();
            driver.OnCardCompleted();
            driver.OnCardCompleted();
            Assert.IsTrue(driver.IsComplete);
            Assert.AreEqual(-1, driver.PhaseIndex);
            Assert.AreEqual(0, driver.Remaining());
        }

        [Test]
        public void Remaining_NeverNegative_AfterOvershoot()
        {
            // Target 2 (modifier 1), then shrink so a later completion overshoots.
            var mod = new MutableModifier();
            var session = MakeSession(MakePhase(2, 2));
            var driver = new SessionDriver(session, () => mod.Value, null, new FixedRandomSource(0));

            driver.OnCardCompleted();
            mod.Value = 0.5f;             // target now max(1, round(1)) = 1
            Assert.AreEqual(0, driver.Remaining());
            driver.OnCardCompleted();     // overshoots and completes
            Assert.IsTrue(driver.IsComplete);
            Assert.AreEqual(0, driver.Remaining());
        }

        [Test]
        public void EmptyPhases_Constructor_ThrowsLikeBaseline()
        {
            var session = new SessionDefinition { Title = "empty" };
            Assert.Throws<InvalidOperationException>(
                () => new SessionDriver(session, () => 1f, null, new FixedRandomSource()));
        }

        [Test]
        public void NullSession_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionDriver(null));
        }
    }
}
