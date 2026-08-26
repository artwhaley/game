using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    public class ContentModelTests
    {
        [Test]
        public void Definitions_Instantiate_AsInertData()
        {
            var session = new SessionDefinition
            {
                Title = "S",
                Tags = { "tag" },
                PhaseSlots =
                {
                    new PhaseSlotDefinition
                    {
                        Title = "P",
                        Candidates = { new PhaseSlotCandidateDefinition { PhaseId = "phase-p" } }
                    }
                }
            };
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options = { new ChoiceOptionDefinition { Label = "L", ChildActionId = "action-child" } }
            };
            var cutscene = new CutsceneActionDefinition { ResourceId = "cs1" };
            var card = new CardDefinition
            {
                Title = "C",
                Tags = { "a" },
                ActionIds = { "action-stat", "action-choice", "action-cutscene" }
            };
            var deck = new CardDeckDefinition { CardIds = { "card-c" } };
            var content = new GameContentDefinition
            {
                Deck = deck,
                Sessions = { session },
                Cards = { card },
                Actions = { choice, cutscene },
                Resources = { new ResourceDefinition { Id = "cs1", Kind = "cutscene" } }
            };

            Assert.AreEqual("S", session.Title);
            Assert.AreEqual(1, session.PhaseSlots.Count);
            Assert.AreEqual("phase-p", session.PhaseSlots[0].Candidates[0].PhaseId);
            Assert.AreEqual("card-c", deck.CardIds[0]);
            Assert.AreEqual("action-stat", card.ActionIds[0]);
            Assert.AreEqual("action-choice", card.ActionIds[1]);
            Assert.AreEqual("action-child", choice.Options[0].ChildActionId);
            Assert.AreEqual("cs1", cutscene.ResourceId);
            Assert.IsTrue(cutscene.IsBlocking);
            Assert.AreEqual(2, content.Actions.Count);
            Assert.AreEqual(1, content.Resources.Count);
        }

        [Test]
        public void Definitions_AcceptAndPreserve_StableIds()
        {
            var deck = new CardDeckDefinition { Id = "deck-1" };
            var card = new CardDefinition { Id = "card-1", Title = "C" };
            var action = new DebugActionDefinition { Id = "action-1" };
            var option = new ChoiceOptionDefinition { Id = "option-1", Label = "L", ChildActionId = "action-1" };
            var choice = new ChoiceActionDefinition { Id = "choice-1", Options = { option } };
            var phase = new PhaseDefinition { Id = "phase-1" };
            var session = new SessionDefinition
            {
                Id = "session-1",
                PhaseSlots =
                {
                    new PhaseSlotDefinition
                    {
                        Id = "slot-1",
                        Candidates = { new PhaseSlotCandidateDefinition { Id = "cand-1", PhaseId = "phase-1" } }
                    }
                }
            };
            var resource = new ResourceDefinition { Id = "cs:1", Kind = "cutscene" };

            Assert.AreEqual("deck-1", deck.Id);
            Assert.AreEqual("card-1", card.Id);
            Assert.AreEqual("action-1", choice.Options[0].ChildActionId);
            Assert.AreEqual("option-1", choice.Options[0].Id);
            Assert.AreEqual("choice-1", choice.Id);
            Assert.AreEqual("phase-1", session.PhaseSlots[0].Candidates[0].PhaseId);
            Assert.AreEqual("slot-1", session.PhaseSlots[0].Id);
            Assert.AreEqual("session-1", session.Id);
            Assert.AreEqual("cs:1", resource.Id);
        }
    }
}
