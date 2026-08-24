using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// EditMode tests for deck filtering and executor sequencing. Run via
    /// Window → General → Test Runner (EditMode). Requires com.unity.test-framework.
    /// </summary>
    public class ExecutorTests
    {
        // ---------- helpers ----------

        /// <summary>Test action that records start/end in order, pausing one step in between.</summary>
        private sealed class TestAction : CardAction
        {
            public string Id;
            public List<string> Order;

            public override IEnumerator Execute(GameContext context)
            {
                Order.Add(Id + "-start");
                yield return null;
                Order.Add(Id + "-end");
            }
        }

        /// <summary>Captures dispatched continuous actions; steps them once to observe "started".</summary>
        private sealed class RecordingRunner : ICoroutineRunner
        {
            public readonly List<IEnumerator> Routines = new List<IEnumerator>();

            public void StartRoutine(IEnumerator routine)
            {
                Routines.Add(routine);
                routine.MoveNext();
            }
        }

        private static Card MakeCard(string[] tags, params CardAction[] actions)
        {
            var card = ScriptableObject.CreateInstance<Card>();
            var so = new SerializedObject(card);
            so.FindProperty("title").stringValue = "TestCard";
            var tagsProp = so.FindProperty("tags");
            tagsProp.arraySize = tags.Length;
            for (var i = 0; i < tags.Length; i++)
            {
                tagsProp.GetArrayElementAtIndex(i).stringValue = tags[i];
            }
            var actionsProp = so.FindProperty("actions");
            actionsProp.arraySize = actions.Length;
            for (var i = 0; i < actions.Length; i++)
            {
                actionsProp.GetArrayElementAtIndex(i).objectReferenceValue = actions[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return card;
        }

        private static CardDeck MakeDeck(params Card[] cards)
        {
            var deck = ScriptableObject.CreateInstance<CardDeck>();
            var so = new SerializedObject(deck);
            var prop = so.FindProperty("cards");
            prop.arraySize = cards.Length;
            for (var i = 0; i < cards.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return deck;
        }

        private static TestAction MakeTestAction(bool blocking, string id, List<string> order)
        {
            var action = ScriptableObject.CreateInstance<TestAction>();
            action.Id = id;
            action.Order = order;
            var so = new SerializedObject(action);
            so.FindProperty("isBlocking").boolValue = blocking;
            so.ApplyModifiedPropertiesWithoutUndo();
            return action;
        }

        private static CardExecutor MakeExecutor(CardDeck deck, ICoroutineRunner runner, string[] include = null, string[] exclude = null)
        {
            return new CardExecutor(deck, runner, include, exclude);
        }

        // ---------- tag filtering ----------

        [Test]
        public void Draw_WithNoFilters_ReturnsAnyCard()
        {
            var deck = MakeDeck(MakeCard(new[] { "truth" }), MakeCard(new[] { "dare" }));
            var executor = MakeExecutor(deck, new RecordingRunner());

            Assert.IsTrue(executor.TryDrawCard(out var card));
            Assert.IsNotNull(card);
        }

        [Test]
        public void Draw_RequiresAllMustIncludeTags()
        {
            var deck = MakeDeck(
                MakeCard(new[] { "truth" }),
                MakeCard(new[] { "dare" }),
                MakeCard(new[] { "party", "dare" }));
            var executor = MakeExecutor(deck, new RecordingRunner(), new[] { "party", "dare" });

            Assert.IsTrue(executor.TryDrawCard(out var card));
            Assert.IsNotNull(card);
            CollectionAssert.Contains(card.Tags, "party");
        }

        [Test]
        public void Draw_ExcludesCardsWithMustExcludeTags()
        {
            var deck = MakeDeck(
                MakeCard(new[] { "truth" }),
                MakeCard(new[] { "dare" }),
                MakeCard(new[] { "party" }));
            var executor = MakeExecutor(deck, new RecordingRunner(), null, new[] { "dare" });

            Assert.IsTrue(executor.TryDrawCard(out var card));
            Assert.IsFalse(card.Tags.Contains("dare"));
        }

        [Test]
        public void Draw_ReturnsFalse_WhenNothingMatches()
        {
            var deck = MakeDeck(MakeCard(new[] { "dare" }));
            var executor = MakeExecutor(deck, new RecordingRunner(), new[] { "truth" }, new[] { "dare" });

            Assert.IsFalse(executor.TryDrawCard(out var card));
            Assert.IsNull(card);
        }

        // ---------- executor sequencing ----------

        [UnityTest]
        public IEnumerator BlockingActions_RunInOrder_AndComplete()
        {
            var order = new List<string>();
            var a1 = MakeTestAction(true, "a1", order);
            var a2 = MakeTestAction(true, "a2", order);
            var card = MakeCard(new[] { "truth" }, a1, a2);
            var deck = MakeDeck(card);
            var executor = MakeExecutor(deck, new RecordingRunner());

            yield return executor.ExecuteCard(card, new GameContext(new Player("Test")));

            CollectionAssert.AreEqual(new[] { "a1-start", "a1-end", "a2-start", "a2-end" }, order);
        }

        [UnityTest]
        public IEnumerator ContinuousAction_IsDispatched_WithoutWaiting()
        {
            var order = new List<string>();
            var a1 = MakeTestAction(false, "a1", order);
            var a2 = MakeTestAction(true, "a2", order);
            var card = MakeCard(new[] { "truth" }, a1, a2);
            var deck = MakeDeck(card);
            var executor = MakeExecutor(deck, new RecordingRunner());

            yield return executor.ExecuteCard(card, new GameContext(new Player("Test")));

            // a1 was handed to the runner (started, not awaited); a2 was awaited to completion.
            CollectionAssert.AreEqual(new[] { "a1-start", "a2-start", "a2-end" }, order);
        }

        // ---------- seed actions ----------

        [Test]
        public void StatIncreaseAction_AddsToPlayersStat()
        {
            var action = ScriptableObject.CreateInstance<StatIncreaseAction>();
            var so = new SerializedObject(action);
            so.FindProperty("statKey").stringValue = "courage";
            so.FindProperty("amount").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();

            var player = new Player("Test");
            var routine = action.Execute(new GameContext(player));
            while (routine.MoveNext()) { }

            Assert.AreEqual(3, player.Stats.Get("courage"));
        }

        [Test]
        public void DebugAction_WithZeroDelay_CompletesImmediately()
        {
            var action = ScriptableObject.CreateInstance<DebugAction>();
            var so = new SerializedObject(action);
            so.FindProperty("message").stringValue = "hi";
            so.FindProperty("delaySeconds").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var routine = action.Execute(new GameContext(new Player("Test")));

            Assert.IsFalse(routine.MoveNext());
        }
    }
}
