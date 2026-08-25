using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// EditMode tests for SessionDriver: per-phase draw targets, live length
    /// modifier rebaking (grow and shrink mid-phase), early advance on no
    /// matching card, and session completion after the last phase.
    /// </summary>
    public class SessionDriverTests
    {
        private sealed class MutableModifier
        {
            public float Value = 1f;
        }

        private static Phase MakePhase(string title, int min, int max, string[] include = null)
        {
            var phase = ScriptableObject.CreateInstance<Phase>();
            var so = new SerializedObject(phase);
            so.FindProperty("title").stringValue = title;
            so.FindProperty("minCards").intValue = min;
            so.FindProperty("maxCards").intValue = max;
            if (include != null)
            {
                var tagsProp = so.FindProperty("mustIncludeTags");
                tagsProp.arraySize = include.Length;
                for (var i = 0; i < include.Length; i++)
                {
                    tagsProp.GetArrayElementAtIndex(i).stringValue = include[i];
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return phase;
        }

        private static Session MakeSession(string title, params Phase[] phases)
        {
            var session = ScriptableObject.CreateInstance<Session>();
            var so = new SerializedObject(session);
            so.FindProperty("title").stringValue = title;
            var phasesProp = so.FindProperty("phases");
            phasesProp.arraySize = phases.Length;
            for (var i = 0; i < phases.Length; i++)
            {
                phasesProp.GetArrayElementAtIndex(i).objectReferenceValue = phases[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return session;
        }

        /// <summary>Seeded RNG so a phase's base target is deterministic.</summary>
        private static System.Random Seeded()
        {
            return new System.Random(42);
        }

        [Test]
        public void Constructor_FirstPhase_IsCurrent_WithItsFilter()
        {
            var session = MakeSession("S",
                MakePhase("P1", 2, 2, new[] { "truth" }),
                MakePhase("P2", 3, 3, new[] { "dare" }));
            var driver = new SessionDriver(session, () => 1f, null, Seeded());

            Assert.IsFalse(driver.IsComplete);
            Assert.AreEqual(0, driver.PhaseIndex);
            CollectionAssert.AreEqual(new[] { "truth" }, driver.CurrentMustInclude);
        }

        [Test]
        public void Phase_Advances_AfterScaledTargetDraws()
        {
            // P1: min=max=2 → base target 2. Modifier 1 → advance after 2 draws.
            var session = MakeSession("S",
                MakePhase("P1", 2, 2, new[] { "truth" }),
                MakePhase("P2", 1, 1, new[] { "dare" }));
            var driver = new SessionDriver(session, () => 1f, null, Seeded());

            driver.OnCardCompleted();
            Assert.AreEqual(0, driver.PhaseIndex); // still P1

            driver.OnCardCompleted();
            Assert.AreEqual(1, driver.PhaseIndex); // now P2
            CollectionAssert.AreEqual(new[] { "dare" }, driver.CurrentMustInclude);
            Assert.AreEqual(1, driver.CurrentTarget()); // P2: min=max=1 × 1
        }

        [Test]
        public void LiveModifier_GrowsPhase_WhenIncreasedMidPhase()
        {
            // P1: min=max=3 → base target 3. Modifier 1 → after 3 draws phase ends.
            // Raise modifier to 2 mid-phase → target becomes 6 → phase continues.
            var session = MakeSession("S", MakePhase("P1", 3, 3));
            var mod = new MutableModifier();
            var driver = new SessionDriver(session, () => mod.Value, null, Seeded());

            driver.OnCardCompleted(); // 1/3
            driver.OnCardCompleted(); // 2/3
            mod.Value = 2f;           // target now 6
            driver.OnCardCompleted(); // 3/6 — phase must NOT advance yet
            Assert.AreEqual(0, driver.PhaseIndex);
            Assert.AreEqual(3, driver.Remaining()); // 6 - 3
        }

        [Test]
        public void LiveModifier_ShrinksPhase_AndAdvancesImmediately()
        {
            // P1: min=max=4 → base target 4. Two draws, then modifier 0.5 → target 2 → already past → advance now.
            var session = MakeSession("S",
                MakePhase("P1", 4, 4),
                MakePhase("P2", 1, 1));
            var mod = new MutableModifier();
            var driver = new SessionDriver(session, () => mod.Value, null, Seeded());

            driver.OnCardCompleted(); // 1/4
            driver.OnCardCompleted(); // 2/4
            mod.Value = 0.5f;         // target 2 → the NEXT draw overflows it
            driver.OnCardCompleted(); // 3rd draw ≥ 2 → advance immediately
            Assert.AreEqual(1, driver.PhaseIndex);
        }

        [Test]
        public void NoMatchingCard_AdvancesEarly_WithWarning()
        {
            var warnings = new List<string>();
            var session = MakeSession("S",
                MakePhase("P1", 5, 5),
                MakePhase("P2", 1, 1));
            var driver = new SessionDriver(session, () => 1f, warnings.Add, Seeded());

            driver.OnNoMatchingCard();

            Assert.AreEqual(1, driver.PhaseIndex);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("advancing early", warnings[0]);
        }

        [Test]
        public void LastPhase_Completion_EndsSession()
        {
            var session = MakeSession("S",
                MakePhase("P1", 2, 2),
                MakePhase("P2", 3, 3));
            var driver = new SessionDriver(session, () => 1f, null, Seeded());

            driver.OnCardCompleted();
            driver.OnCardCompleted(); // P1 done → P2
            Assert.AreEqual(1, driver.PhaseIndex);

            driver.OnCardCompleted();
            driver.OnCardCompleted();
            driver.OnCardCompleted(); // P2 done → complete
            Assert.IsTrue(driver.IsComplete);
            Assert.AreEqual(-1, driver.PhaseIndex);
            Assert.AreEqual(0, driver.Remaining());
        }

        [Test]
        public void MinMax_Clamp_Roundtrip()
        {
            // min > max after authoring error → driver clamps to sane behavior, no throw.
            var session = MakeSession("S", MakePhase("P1", 5, 2));
            var driver = new SessionDriver(session, () => 1f, null, Seeded());

            Assert.DoesNotThrow(() => driver.OnCardCompleted());
            Assert.IsTrue(driver.CurrentTarget() >= 1);
        }
    }
}