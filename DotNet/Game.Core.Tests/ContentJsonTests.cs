using System;
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
                AppContext.BaseDirectory, "TestData", "parity-content-v1.json");
        }

        private static ContentDocument LoadFixture()
        {
            return ContentJson.Load(File.ReadAllText(FixturePath()));
        }

        [Test]
        public void Fixture_Loads_WithSchemaVersionAndShape()
        {
            var document = LoadFixture();

            Assert.AreEqual(1, document.SchemaVersion);
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
                  "schemaVersion": 1,
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
                  "schemaVersion": 1,
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
