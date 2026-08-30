using System;
using System.Linq;
using NUnit.Framework;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    public class ToyOutputSimulatorTests
    {
        private sealed class FakeClock
        {
            public long Now;
            public void Advance(long ms) => Now += ms;
        }

        private static ToyOutputSimulator Create(FakeClock clock) => new ToyOutputSimulator(() => clock.Now);

        private const string Cap = "cap-larynx";
        private const string PatternA = "res-pat-aaa";
        private const string PatternB = "res-pat-bbb";

        [Test]
        public void PlayFor_ActivatesOnCapability_ThenClearsOnNaturalExpiry()
        {
            var clock = new FakeClock();
            var sim = Create(clock);

            sim.PlayFor(Cap, PatternA, TimeSpan.FromSeconds(40));
            Assert.AreEqual(PatternA, sim.ActivePattern(Cap));
            Assert.IsTrue(sim.IsTimedActive(Cap));

            clock.Advance(39_000);
            Assert.AreEqual(PatternA, sim.ActivePattern(Cap), "not yet expired");

            clock.Advance(1_500); // 40.5s total
            Assert.IsNull(sim.ActivePattern(Cap), "cleared after natural expiry");
            Assert.IsFalse(sim.HasCapability(Cap));
        }

        [Test]
        public void SupersededTimedCommand_StaleTimer_NeverStopsNewerCommand()
        {
            var clock = new FakeClock();
            var sim = Create(clock);

            var first = sim.PlayFor(Cap, PatternA, TimeSpan.FromSeconds(10));
            var second = sim.PlayFor(Cap, PatternB, TimeSpan.FromSeconds(120)); // supersedes first

            Assert.AreNotEqual(first, second, "superseding bumps the generation");

            // Advance past the FIRST command's original 10s expiry.
            clock.Advance(10_500);
            Assert.AreEqual(PatternB, sim.ActivePattern(Cap),
                "stale first timer must not clear the newer second command");
            Assert.IsTrue(sim.OwnsGeneration(Cap, second), "second still owns the slot");
        }

        [Test]
        public void CommandsOnDifferentCapabilities_CoexistIndependently()
        {
            var clock = new FakeClock();
            var sim = Create(clock);

            sim.PlayFor("cap-a", PatternA, TimeSpan.FromSeconds(5));
            sim.PlayFor("cap-b", PatternB, TimeSpan.FromSeconds(120));

            Assert.AreEqual(PatternA, sim.ActivePattern("cap-a"));
            Assert.AreEqual(PatternB, sim.ActivePattern("cap-b"));

            clock.Advance(5_500); // cap-a expires, cap-b continues
            Assert.IsNull(sim.ActivePattern("cap-a"));
            Assert.AreEqual(PatternB, sim.ActivePattern("cap-b"));
        }

        [Test]
        public void SetPatternSupersedesTimed_AndSurvivesOldTimerExpiry()
        {
            var clock = new FakeClock();
            var sim = Create(clock);

            sim.PlayFor(Cap, PatternA, TimeSpan.FromSeconds(10));
            sim.SetPattern(Cap, PatternB);

            Assert.AreEqual(PatternB, sim.ActivePattern(Cap));
            Assert.IsFalse(sim.IsTimedActive(Cap), "persistent command is not a background task");

            // The superseded timed command's timer firing later must not erase the persistent pattern.
            clock.Advance(10_500);
            Assert.AreEqual(PatternB, sim.ActivePattern(Cap),
                "stale timed timer must not clear a persistent command that superseded it");
        }

        [Test]
        public void SetPattern_PersistsAcrossTimeAdvances_AndIsNotTimed()
        {
            var clock = new FakeClock();
            var sim = Create(clock);

            sim.SetPattern(Cap, PatternB);
            clock.Advance(3_600_000); // 1 hour
            Assert.AreEqual(PatternB, sim.ActivePattern(Cap));
            Assert.IsFalse(sim.IsTimedActive(Cap));
        }

        [Test]
        public void PlayFor_NewCommand_SupersedesPersistentCommand()
        {
            var clock = new FakeClock();
            var sim = Create(clock);

            sim.SetPattern(Cap, PatternB);
            sim.PlayFor(Cap, PatternA, TimeSpan.FromSeconds(30));

            Assert.AreEqual(PatternA, sim.ActivePattern(Cap));
            Assert.IsTrue(sim.IsTimedActive(Cap));

            clock.Advance(30_500);
            Assert.IsNull(sim.ActivePattern(Cap), "timed command cleared, persistent set was superseded");
        }

        [Test]
        public void StopAll_ClearsEveryCapability_Immediately()
        {
            var clock = new FakeClock();
            var sim = Create(clock);

            sim.PlayFor("cap-a", PatternA, TimeSpan.FromSeconds(60));
            sim.SetPattern("cap-b", PatternB);

            sim.StopAll();

            Assert.IsFalse(sim.HasCapability("cap-a"));
            Assert.IsFalse(sim.HasCapability("cap-b"));
            Assert.IsEmpty(sim.Snapshot());
        }

        [Test]
        public void Snapshot_ReportsActiveTimedStateAndRemaining()
        {
            var clock = new FakeClock();
            var sim = Create(clock);

            sim.PlayFor(Cap, PatternA, TimeSpan.FromSeconds(40));
            sim.SetPattern("cap-persist", PatternB);

            clock.Advance(15_000);

            var snapshot = sim.Snapshot().ToDictionary(s => s.CapabilityId);
            Assert.That(snapshot, Does.ContainKey(Cap));
            Assert.That(snapshot, Does.ContainKey("cap-persist"));

            var timed = snapshot[Cap];
            Assert.IsTrue(timed.IsTimed);
            Assert.AreEqual(PatternA, timed.PatternResourceId);
            Assert.AreEqual(25_000, timed.RemainingMs);

            var persistent = snapshot["cap-persist"];
            Assert.IsFalse(persistent.IsTimed);
            Assert.AreEqual(PatternB, persistent.PatternResourceId);
        }
    }
}