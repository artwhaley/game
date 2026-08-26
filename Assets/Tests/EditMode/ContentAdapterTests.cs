using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// EditMode tests for ScriptableObject → portable definition conversion
    /// (Ticket 07). Complements the portable engine suite: these prove the
    /// serialized asset shells convert faithfully, including the cutscene
    /// registry bridge.
    /// </summary>
    public class ContentAdapterTests
    {
        private static void Set(ScriptableObject asset, string field, string value)
        {
            var so = new SerializedObject(asset);
            so.FindProperty(field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(ScriptableObject asset, string field, float value)
        {
            var so = new SerializedObject(asset);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(ScriptableObject asset, string field, int value)
        {
            var so = new SerializedObject(asset);
            so.FindProperty(field).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(ScriptableObject asset, string field, bool value)
        {
            var so = new SerializedObject(asset);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetStringList(ScriptableObject asset, string field, string[] values)
        {
            var so = new SerializedObject(asset);
            var prop = so.FindProperty(field);
            prop.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).stringValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectList(SerializedObject so, string field, Object[] values)
        {
            var prop = so.FindProperty(field);
            prop.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- actions ----------

        [Test]
        public void DebugAction_Converts_Fields()
        {
            var action = ScriptableObject.CreateInstance<DebugAction>();
            Set(action, "message", "hello");
            Set(action, "delaySeconds", 1.5f);
            Set(action, "isBlocking", true);

            var definition = (Content.DebugActionDefinition)action.ToDefinition(null);

            Assert.IsTrue(definition.IsBlocking);
            Assert.AreEqual("hello", definition.Message);
            Assert.AreEqual(1.5f, definition.DelaySeconds);
        }

        [Test]
        public void StatAction_Converts_Fields()
        {
            var action = ScriptableObject.CreateInstance<StatIncreaseAction>();
            Set(action, "statKey", "courage");
            Set(action, "amount", 7);
            Set(action, "isBlocking", false);

            var definition = (Content.StatIncreaseActionDefinition)action.ToDefinition(null);

            Assert.IsFalse(definition.IsBlocking);
            Assert.AreEqual("courage", definition.StatKey);
            Assert.AreEqual(7, definition.Amount);
        }

        [Test]
        public void ChoiceAction_Converts_Recursively_WithNullChild()
        {
            var childA = ScriptableObject.CreateInstance<DebugAction>();
            Set(childA, "message", "a");
            var choice = ScriptableObject.CreateInstance<ChoiceAction>();
            Set(choice, "prompt", "Pick");
            var so = new SerializedObject(choice);
            var options = so.FindProperty("options");
            options.arraySize = 2;
            options.GetArrayElementAtIndex(0).FindPropertyRelative("label").stringValue = "A";
            options.GetArrayElementAtIndex(0).FindPropertyRelative("action").objectReferenceValue = childA;
            options.GetArrayElementAtIndex(1).FindPropertyRelative("label").stringValue = "B";
            options.GetArrayElementAtIndex(1).FindPropertyRelative("action").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            var definition = (Content.ChoiceActionDefinition)choice.ToDefinition(null);

            Assert.AreEqual("Pick", definition.Prompt);
            Assert.AreEqual(2, definition.Options.Count);
            Assert.AreEqual("A", definition.Options[0].Label);
            Assert.IsInstanceOf<Content.DebugActionDefinition>(definition.Options[0].Child);
            Assert.IsNull(definition.Options[1].Child);
        }

        [Test]
        public void CutsceneAction_NullTimeline_ConvertsToNullResourceId()
        {
            var action = ScriptableObject.CreateInstance<CutsceneAction>();
            Set(action, "isBlocking", true);

            var definition = (Content.CutsceneActionDefinition)action.ToDefinition(new CutsceneBindingRegistry());

            Assert.IsTrue(definition.IsBlocking);
            Assert.That(definition.ResourceId, Is.Null.Or.Empty);
        }

        [Test]
        public void CutsceneAction_WithRegistry_RegistersAndResolves()
        {
            var action = ScriptableObject.CreateInstance<CutsceneAction>();
            Set(action, "isBlocking", true);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var so = new SerializedObject(action);
            so.FindProperty("timeline").objectReferenceValue = timeline;
            so.ApplyModifiedPropertiesWithoutUndo();

            var registry = new CutsceneBindingRegistry();
            var definition = (Content.CutsceneActionDefinition)action.ToDefinition(registry);

            Assert.That(definition.ResourceId, Does.StartWith("cutscene:"));
            Assert.IsTrue(registry.TryResolve(definition.ResourceId, out var resolved));
            Assert.AreSame(timeline, resolved);
        }

        // ---------- cards / deck / session ----------

        [Test]
        public void Card_Converts_Title_Tags_Actions_PreservingOrderAndNulls()
        {
            var debug = ScriptableObject.CreateInstance<DebugAction>();
            Set(debug, "message", "d");
            var card = ScriptableObject.CreateInstance<Card>();
            Set(card, "title", "The Card");
            SetStringList(card, "tags", new[] { "party", "truth" });
            var so = new SerializedObject(card);
            SetObjectList(so, "actions", new Object[] { null, debug });

            var definition = card.ToDefinition(null);

            Assert.AreEqual("The Card", definition.Title);
            CollectionAssert.AreEqual(new[] { "party", "truth" }, definition.Tags.ToArray());
            Assert.AreEqual(2, definition.Actions.Count);
            Assert.IsNull(definition.Actions[0]);
            Assert.IsInstanceOf<Content.DebugActionDefinition>(definition.Actions[1]);
        }

        [Test]
        public void Deck_Converts_PreservingOrderAndNulls()
        {
            var cardA = ScriptableObject.CreateInstance<Card>();
            Set(cardA, "title", "A");
            var deck = ScriptableObject.CreateInstance<CardDeck>();
            var so = new SerializedObject(deck);
            SetObjectList(so, "cards", new Object[] { cardA, null });

            var definition = deck.ToDefinition(null);

            Assert.AreEqual(2, definition.Cards.Count);
            Assert.AreEqual("A", definition.Cards[0].Title);
            Assert.IsNull(definition.Cards[1]);
        }

        [Test]
        public void SessionAndPhases_Convert_WithTagsMinMax()
        {
            var phase = ScriptableObject.CreateInstance<Phase>();
            Set(phase, "title", "Warm Up");
            Set(phase, "minCards", 2);
            Set(phase, "maxCards", 5);
            SetStringList(phase, "mustIncludeTags", new[] { "solo" });
            SetStringList(phase, "mustExcludeTags", new[] { "loud" });

            var session = ScriptableObject.CreateInstance<Session>();
            Set(session, "title", "Relaxing");
            SetStringList(session, "tags", new[] { "relaxing" });
            var so = new SerializedObject(session);
            SetObjectList(so, "phases", new Object[] { phase });

            var definition = session.ToDefinition();

            Assert.AreEqual("Relaxing", definition.Title);
            CollectionAssert.AreEqual(new[] { "relaxing" }, definition.Tags.ToArray());
            Assert.AreEqual(1, definition.Phases.Count);
            var converted = definition.Phases[0];
            Assert.AreEqual("Warm Up", converted.Title);
            Assert.AreEqual(2, converted.MinCards);
            Assert.AreEqual(5, converted.MaxCards);
            CollectionAssert.AreEqual(new[] { "solo" }, converted.MustIncludeTags.ToArray());
            CollectionAssert.AreEqual(new[] { "loud" }, converted.MustExcludeTags.ToArray());
        }
    }
}
