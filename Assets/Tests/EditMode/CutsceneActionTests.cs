using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using UnityEditor;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// EditMode tests for CutsceneAction. Verifies the blocking contract:
    /// the action yields while the cutscene player reports playing, and
    /// returns control once it stops. Uses a fake ICutscenePlayer so no scene
    /// or Timeline asset is needed.
    /// </summary>
    public class CutsceneActionTests
    {
        /// <summary>Fake ICutscenePlayer whose playing state we drive by hand.</summary>
        private sealed class FakeCutscenePlayer : ICutscenePlayer
        {
            public float PlayedCount;
            public bool IsPlayingValue;
            public bool IsPlaying => IsPlayingValue;
            public bool Initialized;

            public void Play(PlayableAsset timeline)
            {
                Initialized = true;
                PlayedCount++;
                IsPlayingValue = true;
            }

            /// <summary>Steps the cutscene "forward" — after the first step the cutscene is done.</summary>
            public void FinishNextFrame()
            {
                IsPlayingValue = false;
            }
        }

        /// <summary>A context whose Services.Cutscene is the given player.</summary>
        private static GameContext MakeContext(ICutscenePlayer player)
        {
            var services = new GameServices(cutscene: player);
            return new GameContext(new Player("cutscene-test"), services);
        }

        private static CutsceneAction MakeAction(bool hasTimeline)
        {
            var action = ScriptableObject.CreateInstance<CutsceneAction>();
            if (hasTimeline)
            {
                // Creating a real TimelineAsset is fine headlessly; only gameplay needs the Timeline window.
                var tl = ScriptableObject.CreateInstance<TimelineAsset>();
                var so = new SerializedObject(action);
                so.FindProperty("timeline").objectReferenceValue = tl;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return action;
        }

        [UnityTest]
        public IEnumerator Execute_YieldsWhilePlaying_AndReturnsWhenDone()
        {
            var player = new FakeCutscenePlayer();
            var action = MakeAction(true);
            var routine = action.Execute(MakeContext(player));

            // First MoveNext runs the initial yield; play hasn't resolved yet.
            Assert.IsTrue(routine.MoveNext());
            Assert.AreEqual(1, player.PlayedCount);
            Assert.IsTrue(player.IsPlaying);

            // While playing, the coroutine stays suspended.
            Assert.IsTrue(routine.MoveNext());

            // Cutscene finishes; next step releases control.
            player.FinishNextFrame();
            Assert.IsFalse(routine.MoveNext());
            yield break;
        }

        [Test]
        public void Execute_WithoutCutscenePlayer_CompletesWithoutHanging()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no ICutscenePlayer"));
            var action = MakeAction(true);
            var routine = action.Execute(MakeContext(null));
            // Should bail out loudly (log), not hang: drain until it completes.
            var steps = 0;
            while (routine.MoveNext())
            {
                steps++;
                if (steps > 10) break;
            }
            // The no-player path yields once then returns; never loops forever.
            Assert.LessOrEqual(steps, 3);
        }

        [Test]
        public void Execute_WithoutTimeline_CompletesWithoutHanging()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no timeline assigned"));
            var player = new FakeCutscenePlayer();
            var action = MakeAction(false); // timeline not assigned
            var routine = action.Execute(MakeContext(player));
            var steps = 0;
            while (routine.MoveNext())
            {
                steps++;
                if (steps > 10) break;
            }
            Assert.LessOrEqual(steps, 3);
        }
    }
}