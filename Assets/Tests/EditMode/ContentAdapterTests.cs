using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// EditMode tests for ScriptableObject → portable reference graph
    /// conversion through UnityContentGraphBuilder (Ticket 11). Proves the
    /// serialized asset shells convert to the v2 shape: action occurrences
    /// become owned instances, cards own sequences with the default progress
    /// instance, phases build the standard executable graph, sessions compose
    /// PhaseReferences, shared-SO-once collection, duplicate-id failures, and
    /// the cutscene registry bridge.
    /// </summary>
    public class ContentAdapterTests
    {
        private static UnityContentGraphBuilder Builder()
        {
            return new UnityContentGraphBuilder(new CutsceneBindingRegistry());
        }

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

        // ---------- actions -> instances ----------

        [Test]
        public void DebugAction_Converts_Fields()
        {
            var action = ScriptableObject.CreateInstance<DebugAction>();
            Set(action, "message", "hello");
            Set(action, "delaySeconds", 1.5f);
            Set(action, "isBlocking", true);

            var definition = (Content.DebugInstanceDefinition)action.ToDefinition(Builder());

            Assert.IsTrue(definition.IsBlocking);
            Assert.AreEqual("hello", definition.Message);
            Assert.AreEqual(1.5f, definition.DelaySeconds);
        }

        [Test]
        public void Action_Converts_WithStableId()
        {
            var action = ScriptableObject.CreateInstance<DebugAction>();
            action.EnsureId();

            var definition = (Content.DebugInstanceDefinition)action.ToDefinition(Builder());

            Assert.AreEqual(action.Id, definition.Id);
            Assert.That(definition.Id, Is.Not.Empty);
        }

        [Test]
        public void ContentId_MintsOnce_AndNeverRegenerates()
        {
            var card = ScriptableObject.CreateInstance<Card>();

            card.EnsureId();
            var first = card.Id;
            card.EnsureId();

            Assert.AreEqual(first, card.Id);
            StringAssert.IsMatch(@"^[0-9a-f]{32}$", card.Id);
        }

        [Test]
        public void ContentId_IsPerInstance_Unique()
        {
            var a = ScriptableObject.CreateInstance<Card>();
            var b = ScriptableObject.CreateInstance<Card>();
            a.EnsureId();
            b.EnsureId();
            Assert.AreNotEqual(a.Id, b.Id);
        }

        [Test]
        public void StatAction_Converts_Fields()
        {
            var action = ScriptableObject.CreateInstance<StatIncreaseAction>();
            Set(action, "statKey", "courage");
            Set(action, "amount", 7);
            Set(action, "isBlocking", false);

            var definition = (Content.StatIncreaseInstanceDefinition)action.ToDefinition(Builder());

            Assert.IsFalse(definition.IsBlocking);
            Assert.AreEqual("courage", definition.StatKey);
            Assert.AreEqual(7, definition.Amount);
        }

        [Test]
        public void ChoiceAction_Converts_Options_WithOwnedSequences()
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

            var definition = (Content.PromptChoiceInstanceDefinition)choice.ToDefinition(Builder());

            Assert.AreEqual("Pick", definition.Prompt);
            Assert.AreEqual(2, definition.Options.Count);
            Assert.AreEqual("A", definition.Options[0].Label);
            Assert.AreEqual(1, definition.Options[0].Sequence.Instances.Count);
            Assert.AreEqual(childA.Id, definition.Options[0].Sequence.Instances[0].Id,
                "the child action became an owned instance in the option's sequence");
            Assert.AreEqual(0, definition.Options[1].Sequence.Instances.Count, "null child -> empty option sequence");
        }

        [Test]
        public void ChoiceAction_Converts_WithStableIds_OnChoiceAndOptions()
        {
            var child = ScriptableObject.CreateInstance<DebugAction>();
            child.EnsureId();
            var choice = ScriptableObject.CreateInstance<ChoiceAction>();
            Set(choice, "prompt", "Pick");
            var so = new SerializedObject(choice);
            var options = so.FindProperty("options");
            options.arraySize = 2;
            options.GetArrayElementAtIndex(0).FindPropertyRelative("label").stringValue = "A";
            options.GetArrayElementAtIndex(0).FindPropertyRelative("action").objectReferenceValue = child;
            options.GetArrayElementAtIndex(1).FindPropertyRelative("label").stringValue = "B";
            so.ApplyModifiedPropertiesWithoutUndo();
            choice.EnsureId(); // mints the choice id and both option ids

            var definition = (Content.PromptChoiceInstanceDefinition)choice.ToDefinition(Builder());

            Assert.AreEqual(choice.Id, definition.Id);
            Assert.AreEqual(choice.Options[0].Id, definition.Options[0].Id);
            Assert.AreEqual(choice.Options[1].Id, definition.Options[1].Id);
            Assert.That(definition.Options[0].Id, Is.Not.Empty);
            Assert.That(definition.Options[1].Id, Is.Not.Empty);
        }

        [Test]
        public void CutsceneAction_NullTimeline_ConvertsToNullResourceId()
        {
            var action = ScriptableObject.CreateInstance<CutsceneAction>();
            Set(action, "isBlocking", true);

            var builder = Builder();
            var definition = (Content.CutsceneInstanceDefinition)action.ToDefinition(builder);

            Assert.IsTrue(definition.IsBlocking);
            Assert.That(definition.ResourceId, Is.Null.Or.Empty);
            Assert.IsEmpty(builder.Resources);
        }

        [Test]
        public void CutsceneAction_AuthoredResourceId_RegistersResolves_AndCollectsResource()
        {
            var action = ScriptableObject.CreateInstance<CutsceneAction>();
            Set(action, "isBlocking", true);
            Set(action, "resourceId", "cs:familiar_face");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var so = new SerializedObject(action);
            so.FindProperty("timeline").objectReferenceValue = timeline;
            so.ApplyModifiedPropertiesWithoutUndo();

            var registry = new CutsceneBindingRegistry();
            var builder = new UnityContentGraphBuilder(registry);
            var definition = (Content.CutsceneInstanceDefinition)action.ToDefinition(builder);

            Assert.AreEqual("cs:familiar_face", definition.ResourceId);
            Assert.IsTrue(registry.TryResolve(definition.ResourceId, out var resolved));
            Assert.AreSame(timeline, resolved);
            Assert.AreEqual(1, builder.Resources.Count);
            Assert.AreEqual("cutscene", builder.Resources[0].Kind);
        }

        [Test]
        public void CutsceneAction_MintsStableGuid_WhenNoAuthoredId()
        {
            var action = ScriptableObject.CreateInstance<CutsceneAction>();
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var so = new SerializedObject(action);
            so.FindProperty("timeline").objectReferenceValue = timeline;
            so.ApplyModifiedPropertiesWithoutUndo();

            var registry = new CutsceneBindingRegistry();
            var builder = new UnityContentGraphBuilder(registry);
            var definition = (Content.CutsceneInstanceDefinition)action.ToDefinition(builder);

            StringAssert.IsMatch(@"^[0-9a-f]{32}$", definition.ResourceId);
            Assert.IsTrue(registry.TryResolve(definition.ResourceId, out var resolved));
            Assert.AreSame(timeline, resolved);
        }

        [Test]
        public void CutsceneAction_AuthoredResourceId_IsStableAcrossConversions()
        {
            var action = ScriptableObject.CreateInstance<CutsceneAction>();
            Set(action, "resourceId", "cs:intro");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var so = new SerializedObject(action);
            so.FindProperty("timeline").objectReferenceValue = timeline;
            so.ApplyModifiedPropertiesWithoutUndo();

            var first = (Content.CutsceneInstanceDefinition)action.ToDefinition(Builder());
            var second = (Content.CutsceneInstanceDefinition)action.ToDefinition(Builder());

            Assert.AreEqual("cs:intro", first.ResourceId);
            Assert.AreEqual(first.ResourceId, second.ResourceId);
        }

        [Test]
        public void CutsceneRegistry_DuplicateResourceId_FailsLoudly()
        {
            var registry = new CutsceneBindingRegistry();
            registry.Register("cs:dup", ScriptableObject.CreateInstance<TimelineAsset>());

            Assert.Throws<System.InvalidOperationException>(() => registry.Register("cs:dup", ScriptableObject.CreateInstance<TimelineAsset>()));
        }

        // ---------- cards / deck / session ----------

        [Test]
        public void Card_Converts_OwnedSequence_WithDefaultProgress()
        {
            var debug = ScriptableObject.CreateInstance<DebugAction>();
            Set(debug, "message", "d");
            var card = ScriptableObject.CreateInstance<Card>();
            card.EnsureId();
            Set(card, "title", "The Card");
            SetStringList(card, "tags", new[] { "party", "truth" });
            var so = new SerializedObject(card);
            SetObjectList(so, "actions", new Object[] { null, debug });

            var builder = Builder();
            var definition = card.ToDefinition(builder);

            Assert.AreEqual(card.Id, definition.Id);
            Assert.AreEqual("The Card", definition.Title);
            CollectionAssert.AreEqual(new[] { "party", "truth" }, definition.Tags.ToArray());
            // Null entries skipped; the authored action becomes an instance; the
            // v2 default progress instance is appended so phases can complete.
            Assert.AreEqual(2, definition.Sequence.Instances.Count);
            Assert.AreEqual(debug.Id, definition.Sequence.Instances[0].Id);
            Assert.IsInstanceOf<Content.IncrementProgressInstanceDefinition>(definition.Sequence.Instances[1]);
        }

        [Test]
        public void Deck_Converts_OrderedCardIds_Dense()
        {
            var cardA = ScriptableObject.CreateInstance<Card>();
            cardA.EnsureId();
            Set(cardA, "title", "A");
            var deck = ScriptableObject.CreateInstance<CardDeck>();
            deck.EnsureId();
            var so = new SerializedObject(deck);
            SetObjectList(so, "cards", new Object[] { cardA, null });

            var builder = Builder();
            var definition = deck.ToDefinition(builder);

            Assert.AreEqual(deck.Id, definition.Id);
            CollectionAssert.AreEqual(new[] { cardA.Id }, definition.CardIds.ToArray());
        }

        [Test]
        public void SessionAndPhases_Convert_ToReferenceGraph_WithCollectedPhases()
        {
            var phase = ScriptableObject.CreateInstance<Phase>();
            phase.EnsureId();
            Set(phase, "title", "Warm Up");
            Set(phase, "minCards", 2);
            Set(phase, "maxCards", 5);
            SetStringList(phase, "mustIncludeTags", new[] { "solo" });
            SetStringList(phase, "mustExcludeTags", new[] { "loud" });

            var session = ScriptableObject.CreateInstance<Session>();
            session.EnsureId();
            Set(session, "title", "Relaxing");
            SetStringList(session, "tags", new[] { "relaxing" });
            var so = new SerializedObject(session);
            SetObjectList(so, "phases", new Object[] { phase });

            var builder = Builder();
            var definition = session.ToDefinition(builder);

            Assert.AreEqual(session.Id, definition.Id);
            Assert.AreEqual("Relaxing", definition.Title);
            CollectionAssert.AreEqual(new[] { "relaxing" }, definition.Tags.ToArray());

            // One Start, one End, one PhaseReference with a projected Complete socket.
            Assert.AreEqual(1, definition.Graph.Nodes.Count(n => n is Content.SessionStartNodeDefinition));
            Assert.AreEqual(1, definition.Graph.Nodes.Count(n => n is Content.SessionEndNodeDefinition));
            var reference = definition.Graph.Nodes.OfType<Content.PhaseReferenceNodeDefinition>().Single();
            Assert.AreEqual(phase.Id, reference.PhaseId);
            Assert.AreEqual(1, reference.Outputs.Count);
            Assert.AreEqual(Content.GraphPortKind.PhaseExit, reference.Outputs[0].Kind);
            Assert.AreEqual(2, definition.Graph.Edges.Count, "start->ref and ref-complete->end");

            var collected = builder.Phases.Single(p => p.Id == phase.Id);
            Assert.AreEqual("Warm Up", collected.Title);
            Assert.AreEqual(1, collected.Exits.Count);
            Assert.AreEqual("Complete", collected.Exits[0].Name);
            Assert.AreEqual(4, collected.Graph.Nodes.Count, "standard graph: entry, executor, check, goto");
            Assert.Greater(collected.Graph.Edges.Count, 0);
            CollectionAssert.AreEqual(new[] { "solo" }, collected.MustIncludeTags.ToArray());
            CollectionAssert.AreEqual(new[] { "loud" }, collected.MustExcludeTags.ToArray());
        }

        [Test]
        public void DuplicatePhaseId_AcrossDistinctAssets_FailsLoudly()
        {
            // Entity-level (Phase/Card) collection still dedups by ID and fails
            // loudly on distinct assets claiming the same ID. Action instances
            // are owned occurrences and never share, so only entities collide.
            var first = ScriptableObject.CreateInstance<Phase>();
            var second = ScriptableObject.CreateInstance<Phase>();
            Set(first, "id", "same-id");
            Set(second, "id", "same-id");
            var session = ScriptableObject.CreateInstance<Session>();
            session.EnsureId();
            var so = new SerializedObject(session);
            SetObjectList(so, "phases", new Object[] { first, second });

            var builder = Builder();
            Assert.Throws<System.InvalidOperationException>(() => session.ToDefinition(builder));
        }

        [Test]
        public void Build_CollectsCompleteGraph_Once()
        {
            var debug = ScriptableObject.CreateInstance<DebugAction>();
            Set(debug, "message", "d");
            var card = ScriptableObject.CreateInstance<Card>();
            card.EnsureId();
            Set(card, "title", "C");
            var cardSo = new SerializedObject(card);
            SetObjectList(cardSo, "actions", new Object[] { debug, debug });

            var phase = ScriptableObject.CreateInstance<Phase>();
            phase.EnsureId();
            Set(phase, "title", "P");

            var session = ScriptableObject.CreateInstance<Session>();
            session.EnsureId();
            Set(session, "title", "S");
            var sessionSo = new SerializedObject(session);
            SetObjectList(sessionSo, "phases", new Object[] { phase });

            var deck = ScriptableObject.CreateInstance<CardDeck>();
            deck.EnsureId();
            var deckSo = new SerializedObject(deck);
            SetObjectList(deckSo, "cards", new Object[] { card });

            var builder = Builder();
            var content = builder.Build(session, deck);

            Assert.AreEqual(1, content.Sessions.Count);
            Assert.AreEqual(1, content.Phases.Count);
            Assert.AreEqual(1, content.Cards.Count);
            Assert.AreEqual(1, content.Deck.CardIds.Count);
            // Same SO referenced twice on one card -> two owned instances (never shared).
            Assert.AreEqual(2, content.Cards[0].Sequence.Instances.Count(i => i.Id == debug.Id));
        }
    }
}
