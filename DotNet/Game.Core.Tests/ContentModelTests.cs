using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    public class ContentModelTests
    {
        [Test]
        public void Definitions_Instantiate_AsInertData()
        {
            var choiceChild = new DebugActionDefinition { Message = "child", DelaySeconds = 0f, IsBlocking = true };
            var session = new SessionDefinition
            {
                Title = "S",
                Tags = { "tag" },
                Phases =
                {
                    new PhaseDefinition
                    {
                        Title = "P",
                        MustIncludeTags = { "a", "b" },
                        MustExcludeTags = { "c" },
                        MinCards = 2,
                        MaxCards = 5
                    }
                }
            };
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options = { new ChoiceOptionDefinition { Label = "L", Child = choiceChild } }
            };
            var deck = new CardDeckDefinition
            {
                Cards =
                {
                    new CardDefinition
                    {
                        Title = "C",
                        Tags = { "a" },
                        Actions =
                        {
                            null,
                            new StatIncreaseActionDefinition { StatKey = "courage", Amount = 2 },
                            choice,
                            new CutsceneActionDefinition { ResourceId = "cs1" }
                        }
                    },
                    null
                }
            };

            Assert.AreEqual("S", session.Title);
            Assert.AreEqual(1, session.Phases.Count);
            Assert.AreEqual(new[] { "a", "b" }, session.Phases[0].MustIncludeTags.ToArray());
            Assert.AreEqual(2, session.Phases[0].MinCards);
            Assert.AreEqual(5, session.Phases[0].MaxCards);

            Assert.IsNull(deck.Cards[1]);
            var card = deck.Cards[0];
            Assert.IsNull(card.Actions[0]);
            Assert.IsInstanceOf<StatIncreaseActionDefinition>(card.Actions[1]);
            Assert.AreEqual(2, ((StatIncreaseActionDefinition)card.Actions[1]).Amount);
            Assert.AreSame(choice, card.Actions[2]);
            Assert.AreSame(choiceChild, ((ChoiceActionDefinition)card.Actions[2]).Options[0].Child);
            Assert.AreEqual("cs1", ((CutsceneActionDefinition)card.Actions[3]).ResourceId);
            Assert.IsTrue(((CutsceneActionDefinition)card.Actions[3]).IsBlocking);
        }
    }
}
