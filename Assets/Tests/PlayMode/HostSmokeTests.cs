using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using TruthCardGame.Core;
using TruthCardGame.Content;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// Narrow PlayMode smoke test (Ticket 10): proves the portable Core runs
    /// inside the live Unity runtime through the real host adapters — scaled
    /// time delay, UnityEngine.Random-backed card draws, wrapper-to-definition
    /// conversion — and completes with no unobserved background faults.
    /// Deliberately independent of authored scenes/Timeline content.
    ///
    /// Advances are started on the main thread (no Task.Run anywhere): the
    /// engine's synchronous parts run inline, its awaits resume through the
    /// Unity synchronization context while the iterator polls.
    /// </summary>
    public class HostSmokeTests
    {
        private sealed class RecordingLog : IGameLog
        {
            public readonly List<string> Entries = new List<string>();
            public void Info(string m) => Entries.Add("info:" + m);
            public void Warning(string m) => Entries.Add("warn:" + m);
            public void Error(string m) => Entries.Add("error:" + m);
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }

        private static void AssertTaskSucceeded(Task task)
        {
            var failure = task.IsCompletedSuccessfully ? null : task.Exception?.GetBaseException().ToString() ?? "not completed";
            Assert.IsTrue(task.IsCompletedSuccessfully, failure);
        }

        [UnityTest]
        public IEnumerator PortableCore_RunsThroughUnityHostAdapters_AndCompletes()
        {
            UnityEngine.Random.InitState(7);

            var log = new RecordingLog();
            var services = new CoreServices(new UnityGameDelay(), log);

            var phase = new PhaseDefinition { Id = "phase-smoke", Title = "Smoke", MinCards = 1, MaxCards = 1 };
            phase.MustIncludeTags.Add("smoke");

            var stat = new StatIncreaseActionDefinition { Id = "action-stat", StatKey = "courage", Amount = 3 };
            var debug = new DebugActionDefinition { Id = "action-debug", Message = "scaled-time beat", DelaySeconds = 0.05f };

            var card = new CardDefinition { Id = "card-smoke", Title = "Smoke Card", Tags = { "smoke" }, ActionIds = { stat.Id, debug.Id } };

            var session = new SessionDefinition { Id = "session-smoke", Title = "Smoke Session" };
            var slot = new PhaseSlotDefinition { Id = "slot-smoke", Title = "Smoke" };
            slot.Candidates.Add(new PhaseSlotCandidateDefinition { Id = "cand-smoke", PhaseId = phase.Id });
            session.PhaseSlots.Add(slot);

            var content = new GameContentDefinition
            {
                Deck = new CardDeckDefinition { Id = "deck-smoke", CardIds = { card.Id } },
                Sessions = { session },
                Phases = { phase },
                Cards = { card },
                Actions = { stat, debug }
            };

            var engine = new GameSessionEngine(
                content,
                session.Id,
                () => 1f,
                phaseLengthRng: new SystemRandomSource(42),
                cardRng: new UnityRandomSource(),
                services);

            var startTime = Time.time;
            var advance = engine.AdvanceOneCardAsync(CancellationToken.None);
            yield return Await(advance);

            AssertTaskSucceeded(advance);
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, advance.Result.Kind);
            Assert.AreEqual("Smoke Card", advance.Result.Card.Title);
            Assert.AreEqual(3, engine.Player.Stats.Get("courage"));

            // The 0.05 s debug beat really consumed scaled game time via UnityGameDelay.
            Assert.GreaterOrEqual(Time.time - startTime, 0.05f);

            // Engine is idle again; no background work left; nothing faulted.
            Assert.IsFalse(engine.IsBusy);
            Assert.AreEqual(0, engine.PendingBackgroundCount);
            Assert.IsFalse(log.Entries.Exists(e => e.StartsWith("error:")));
        }

        [UnityTest]
        public IEnumerator ConvertedScriptableObjects_FeedCoreEngine_InPlayMode()
        {
            var phaseAsset = ScriptableObject.CreateInstance<Phase>();
            {
                var so = new SerializedObject(phaseAsset);
                so.FindProperty("title").stringValue = "Ending";
                so.FindProperty("minCards").intValue = 1;
                so.FindProperty("maxCards").intValue = 1;
                var tags = so.FindProperty("mustIncludeTags");
                tags.arraySize = 1;
                tags.GetArrayElementAtIndex(0).stringValue = "ending";
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var statAsset = ScriptableObject.CreateInstance<StatIncreaseAction>();
            {
                var so = new SerializedObject(statAsset);
                so.FindProperty("statKey").stringValue = "courage";
                so.FindProperty("amount").intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var cardAsset = ScriptableObject.CreateInstance<Card>();
            {
                var so = new SerializedObject(cardAsset);
                so.FindProperty("title").stringValue = "The End";
                var tags = so.FindProperty("tags");
                tags.arraySize = 1;
                tags.GetArrayElementAtIndex(0).stringValue = "ending";
                var actions = so.FindProperty("actions");
                actions.arraySize = 1;
                actions.GetArrayElementAtIndex(0).objectReferenceValue = statAsset;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var sessionAsset = ScriptableObject.CreateInstance<Session>();
            {
                var so = new SerializedObject(sessionAsset);
                so.FindProperty("title").stringValue = "Converted";
                var phases = so.FindProperty("phases");
                phases.arraySize = 1;
                phases.GetArrayElementAtIndex(0).objectReferenceValue = phaseAsset;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var deckAsset = ScriptableObject.CreateInstance<CardDeck>();
            {
                var so = new SerializedObject(deckAsset);
                var cards = so.FindProperty("cards");
                cards.arraySize = 1;
                cards.GetArrayElementAtIndex(0).objectReferenceValue = cardAsset;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var log = new RecordingLog();
            var services = new CoreServices(new UnityGameDelay(), log);
            var builder = new UnityContentGraphBuilder(new CutsceneBindingRegistry());
            var content = builder.Build(sessionAsset, deckAsset);
            var engine = new GameSessionEngine(
                content,
                sessionAsset.Id,
                () => 1f,
                phaseLengthRng: new SystemRandomSource(5),
                cardRng: new SystemRandomSource(9),
                services);

            var advance = engine.AdvanceOneCardAsync(CancellationToken.None);
            yield return Await(advance);

            AssertTaskSucceeded(advance);
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, advance.Result.Kind);
            Assert.AreEqual("The End", advance.Result.Card.Title);
            Assert.AreEqual(1, engine.Player.Stats.Get("courage"));
            Assert.IsFalse(engine.IsBusy);
            Assert.IsFalse(log.Entries.Exists(e => e.StartsWith("error:")));
        }
    }
}
