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
    /// Narrow PlayMode smoke test (Ticket 11): proves the portable graph VM
    /// runs inside the live Unity runtime through the real host adapters —
    /// scaled time delay, wrapper-to-instance conversion, and the composed
    /// session graph — and completes with no unobserved background faults.
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

        /// <summary>Standard single-exit phase graph: Entry -> Exec -> done(>=100): true -> GOTO; false -> Exec.</summary>
        private static PhaseDefinition StandardPhase(string id, params string[] includeTags)
        {
            var phase = new PhaseDefinition { Id = id, Title = id };
            if (includeTags != null) phase.MustIncludeTags.AddRange(includeTags);
            var exitId = "px-" + id + "-complete";
            phase.Exits.Add(new PhaseExitDefinition { Id = exitId, Name = "Complete" });

            var entry = new PhaseEntryNodeDefinition { Id = "n-" + id + "-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = entry.Id + "-out" });
            var exec = new CardExecutorNodeDefinition { Id = "n-" + id + "-exec" };
            exec.Outputs.Add(new GraphOutputDefinition { Id = exec.Id + "-out" });
            var done = new VariableCheckNodeDefinition
            {
                Id = "n-" + id + "-done",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 100f,
            };
            done.Outputs.Add(new GraphOutputDefinition { Id = done.Id + "-true", Kind = GraphPortKind.True });
            done.Outputs.Add(new GraphOutputDefinition { Id = done.Id + "-false", Kind = GraphPortKind.False });
            var gotoDone = new ActionNodeDefinition
            {
                Id = "n-" + id + "-goto",
                Sequence = new ActionSequenceDefinition
                {
                    Id = "seq-" + id + "-goto",
                    Instances = { new PhaseGotoInstanceDefinition { Id = "inst-" + id + "-goto", PhaseExitId = exitId } },
                },
            };
            gotoDone.Outputs.Add(new GraphOutputDefinition { Id = gotoDone.Id + "-out" });

            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(exec);
            phase.Graph.Nodes.Add(done);
            phase.Graph.Nodes.Add(gotoDone);
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e1", SourceOutputId = entry.Outputs[0].Id, TargetNodeId = exec.Id });
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e2", SourceOutputId = exec.Outputs[0].Id, TargetNodeId = done.Id });
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e3", SourceOutputId = done.Outputs[1].Id, TargetNodeId = exec.Id });
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e4", SourceOutputId = done.Outputs[0].Id, TargetNodeId = gotoDone.Id });
            return phase;
        }

        /// <summary>Start -> ref(phase) -> End with the Complete socket wired to End.</summary>
        private static SessionDefinition SinglePhaseSession(string id, string phaseId)
        {
            var session = new SessionDefinition { Id = id, Title = id, SessionTypeId = "type-standard" };
            var start = new SessionStartNodeDefinition { Id = "n-" + id + "-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = start.Id + "-out" });
            var reference = new PhaseReferenceNodeDefinition { Id = "n-" + id + "-ref", PhaseId = phaseId };
            reference.Outputs.Add(new GraphOutputDefinition
            {
                Id = reference.Id + "-complete",
                Kind = GraphPortKind.PhaseExit,
                PhaseExitId = "px-" + phaseId + "-complete",
            });
            var end = new SessionEndNodeDefinition { Id = "n-" + id + "-end" };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(reference);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se1", SourceOutputId = start.Outputs[0].Id, TargetNodeId = reference.Id });
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se2", SourceOutputId = reference.Outputs[0].Id, TargetNodeId = end.Id });
            return session;
        }

        [UnityTest]
        public IEnumerator PortableCore_RunsThroughUnityHostAdapters_AndCompletes()
        {
            UnityEngine.Random.InitState(7);

            var log = new RecordingLog();
            var services = new CoreServices(new UnityGameDelay(), log);

            var phase = StandardPhase("phase-smoke", "smoke");

            var stat = new StatIncreaseInstanceDefinition { Id = "action-stat", StatKey = "courage", Amount = 3 };
            var debug = new DebugInstanceDefinition { Id = "action-debug", Message = "scaled-time beat", DelaySeconds = 0.05f };
            var card = new CardDefinition { Id = "card-smoke", Title = "Smoke Card", Tags = { "smoke" } };
            card.Sequence.Instances.Add(stat);
            card.Sequence.Instances.Add(debug);
            card.Sequence.Instances.Add(new IncrementProgressInstanceDefinition { Id = "card-progress", Amount = 10f });

            var session = SinglePhaseSession("session-smoke", phase.Id);

            var content = new GameContentDefinition
            {
                Deck = new CardDeckDefinition { Id = "deck-smoke", CardIds = { card.Id } },
                Sessions = { session },
                Phases = { phase },
                Cards = { card },
            };

            var engine = new GameSessionEngine(content, session.Id, services);

            var startTime = Time.time;
            var cards = 0;
            var guard = 0;
            while (!engine.IsComplete && guard++ < 40)
            {
                var advance = engine.AdvanceOneCardAsync(CancellationToken.None);
                yield return Await(advance);
                AssertTaskSucceeded(advance);
                if (advance.Result.Kind == AdvanceResultKind.CardCompleted) cards++;
            }

            Assert.IsTrue(engine.IsComplete, "session completed");
            // Ten +10 cards reach 100; the 10th card's advance transfers and
            // completes the session in the same call, so 9 report CardCompleted.
            Assert.AreEqual(9, cards, "nine card-completed advances plus the completing one");
            Assert.AreEqual(30, engine.Player.Stats.Get("courage"), "3 per card × 10 cards");
            Assert.GreaterOrEqual(Time.time - startTime, 0.05f, "the debug beat really consumed scaled game time");

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
            var engine = new GameSessionEngine(content, sessionAsset.Id, services);

            var guard = 0;
            while (!engine.IsComplete && guard++ < 40)
            {
                var advance = engine.AdvanceOneCardAsync(CancellationToken.None);
                yield return Await(advance);
                AssertTaskSucceeded(advance);
            }

            Assert.IsTrue(engine.IsComplete, "converted session completed through the graph VM");
            // minCards=1/maxCards=1 -> progress target 10 -> the +10 default
            // progress instance completes the phase on the first card.
            Assert.AreEqual(1, engine.Player.Stats.Get("courage"),
                "the ending card's +1 stat ran on its single draw");
            Assert.IsFalse(engine.IsBusy);
            Assert.IsFalse(log.Entries.Exists(e => e.StartsWith("error:")));
        }
    }
}
