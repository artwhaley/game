using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Json;

namespace TruthCardGame.Core.Tests
{
    public class ContentJsonTests
    {
        private static string FixturePath()
        {
            return Path.Combine(
                AppContext.BaseDirectory, "TestData", "parity-content-v2.json");
        }

        private static ContentDocument LoadFixture()
        {
            return ContentJson.Load(File.ReadAllText(FixturePath()));
        }

        [Test]
        public void Fixture_Loads_WithSchemaVersionAndShape()
        {
            var document = LoadFixture();

            Assert.AreEqual(2, document.SchemaVersion);
            Assert.AreEqual(5, document.Deck.Cards.Count); // includes one null entry
            Assert.IsNull(document.Deck.Cards[4]);
            Assert.AreEqual(1, document.Sessions.Count);
            Assert.AreEqual(3, document.Sessions[0].Phases.Count);

            // All four action discriminators present.
            var types = document.Deck.Cards
                .Where(c => c != null)
                .SelectMany(c => c.Actions)
                .Select(a => a.GetType().Name)
                .ToList();
            CollectionAssert.IsSubsetOf(
                new[] { nameof(DebugActionDefinition), nameof(StatIncreaseActionDefinition), nameof(ChoiceActionDefinition), nameof(CutsceneActionDefinition) },
                types);
        }

        [Test]
        public void RecursiveChoiceChild_Deserializes()
        {
            var document = LoadFixture();
            var crossroads = document.Deck.Cards.First(c => c != null && c.Title == "Crossroads");
            var choice = (ChoiceActionDefinition)crossroads.Actions[0];

            Assert.AreEqual("Brave or cautious?", choice.Prompt);
            Assert.AreEqual("Brave", choice.Options[0].Label);
            var brave = (StatIncreaseActionDefinition)choice.Options[0].Child;
            Assert.AreEqual("brave", brave.StatKey);
            Assert.AreEqual(5, brave.Amount);
            Assert.IsNull(choice.Options[1].Child);
        }

        [Test]
        public void RoundTrip_RetainsEquivalentLogicalContent()
        {
            var original = LoadFixture();
            var roundTripped = ContentJson.Load(ContentJson.Save(original));

            Assert.AreEqual(original.SchemaVersion, roundTripped.SchemaVersion);
            Assert.AreEqual(original.Sessions.Count, roundTripped.Sessions.Count);
            Assert.AreEqual("Parity Session", roundTripped.Sessions[0].Title);
            CollectionAssert.AreEqual(
                original.Sessions[0].Phases.Select(p => (p.Title, p.MinCards, p.MaxCards)),
                roundTripped.Sessions[0].Phases.Select(p => (p.Title, p.MinCards, p.MaxCards)));

            var originalEnd = original.Deck.Cards.First(c => c?.Title == "The End");
            var roundTrippedEnd = roundTripped.Deck.Cards.First(c => c?.Title == "The End");
            var originalCutscene = (CutsceneActionDefinition)originalEnd.Actions[0];
            var roundTrippedCutscene = (CutsceneActionDefinition)roundTrippedEnd.Actions[0];
            Assert.AreEqual(originalCutscene.ResourceId, roundTrippedCutscene.ResourceId);
            Assert.IsNull(roundTripped.Deck.Cards[4]);
        }

        [Test]
        public void StableIds_Present_OnEveryEntity_InFixture()
        {
            var document = LoadFixture();

            Assert.That(document.Deck.Id, Is.Not.Empty);
            Assert.That(document.Sessions[0].Id, Is.Not.Empty);
            CollectionAssert.AllItemsAreNotNull(document.Sessions[0].Phases.Select(p => p.Id).ToArray());
            CollectionAssert.AllItemsAreNotNull(document.Sessions[0].Phases.Select(p => p.Title).ToArray());

            foreach (var card in document.Deck.Cards.Where(c => c != null))
            {
                Assert.That(card.Id, Is.Not.Empty);
                foreach (var action in card.Actions.Where(a => a != null))
                {
                    Assert.That(action.Id, Is.Not.Empty);
                    if (action is ChoiceActionDefinition choice)
                    {
                        foreach (var option in choice.Options.Where(o => o != null))
                        {
                            Assert.That(option.Id, Is.Not.Empty);
                            if (option.Child != null) Assert.That(option.Child.Id, Is.Not.Empty);
                        }
                    }
                }
            }
        }

        [Test]
        public void StableIds_AreUnique_AcrossFixture()
        {
            var document = LoadFixture();

            var ids = new List<string> { document.Deck.Id, document.Sessions[0].Id };
            ids.AddRange(document.Sessions[0].Phases.Select(p => p.Id));
            foreach (var card in document.Deck.Cards.Where(c => c != null))
            {
                ids.Add(card.Id);
                foreach (var action in card.Actions.Where(a => a != null))
                {
                    ids.Add(action.Id);
                    if (action is ChoiceActionDefinition choice)
                    {
                        foreach (var option in choice.Options.Where(o => o != null))
                        {
                            ids.Add(option.Id);
                            if (option.Child != null) ids.Add(option.Child.Id);
                        }
                    }
                }
            }

            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "Content IDs must be unique within a document.");
        }

        [Test]
        public void StableIds_RoundTrip_OnAllEntities()
        {
            var original = LoadFixture();
            var roundTripped = ContentJson.Load(ContentJson.Save(original));

            Assert.AreEqual(original.Deck.Id, roundTripped.Deck.Id);
            Assert.AreEqual(original.Sessions[0].Id, roundTripped.Sessions[0].Id);
            CollectionAssert.AreEqual(
                original.Sessions[0].Phases.Select(p => p.Id),
                roundTripped.Sessions[0].Phases.Select(p => p.Id));

            var originalCards = original.Deck.Cards.Where(c => c != null).ToList();
            var roundTrippedCards = roundTripped.Deck.Cards.Where(c => c != null).ToList();
            CollectionAssert.AreEqual(originalCards.Select(c => c.Id), roundTrippedCards.Select(c => c.Id));
            CollectionAssert.AreEqual(
                originalCards.SelectMany(c => c.Actions.Where(a => a != null).Select(a => a.Id)),
                roundTrippedCards.SelectMany(c => c.Actions.Where(a => a != null).Select(a => a.Id)));
        }

        [Test]
        public void SchemaVersion1_WithoutIds_IsRejected()
        {
            const string json = """
                {
                  "schemaVersion": 1,
                  "deck": { "cards": [ { "title": "X", "actions": [] } ] },
                  "sessions": []
                }
                """;

            var ex = Assert.Throws<JsonException>(() => ContentJson.Load(json));
            StringAssert.Contains("schemaVersion 1", ex.Message);
        }

        [Test]
        public void CutsceneResourceId_RoundTrips()
        {
            var document = new ContentDocument
            {
                Deck = new CardDeckDefinition
                {
                    Cards = { new CardDefinition { Title = "C", Actions = { new CutsceneActionDefinition { ResourceId = "res://intro" } } } }
                }
            };

            var back = ContentJson.Load(ContentJson.Save(document));
            var cutscene = (CutsceneActionDefinition)back.Deck.Cards[0].Actions[0];
            Assert.AreEqual("res://intro", cutscene.ResourceId);
        }

        [Test]
        public void UnknownActionType_FailsClearly()
        {
            const string json = """
                {
                  "schemaVersion": 2,
                  "deck": { "cards": [ { "title": "X", "actions": [ { "type": "minigame", "isBlocking": true } ] } ] },
                  "sessions": []
                }
                """;

            var ex = Assert.Throws<JsonException>(() => ContentJson.Load(json));
            StringAssert.Contains("minigame", ex.Message);
        }

        [Test]
        public void MissingDiscriminator_FailsClearly()
        {
            const string json = """
                {
                  "schemaVersion": 2,
                  "deck": { "cards": [ { "title": "X", "actions": [ { "message": "no type here" } ] } ] },
                  "sessions": []
                }
                """;

            Assert.Throws<JsonException>(() => ContentJson.Load(json));
        }

        [Test]
        public void UnsupportedSchemaVersion_FailsClearly()
        {
            const string json = """
                { "schemaVersion": 99, "deck": { "cards": [] }, "sessions": [] }
                """;

            var ex = Assert.Throws<JsonException>(() => ContentJson.Load(json));
            StringAssert.Contains("schemaVersion 99", ex.Message);
        }

        [Test]
        public void EmittedJson_HasNoUnityOrAssemblyNames()
        {
            var json = ContentJson.Save(LoadFixture());

            StringAssert.DoesNotContain("UnityEngine", json);
            StringAssert.DoesNotContain("TruthCardGame,", json);
            StringAssert.DoesNotContain("Assembly", json);
            StringAssert.DoesNotContain("guid", json.ToLowerInvariant());
        }
    }
}
