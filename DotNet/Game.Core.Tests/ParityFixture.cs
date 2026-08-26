using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Small SFW in-memory content fixture exercising all current mechanics:
    /// multiple phases, include filtering, a blocking choice child, a
    /// nonblocking background action, a cutscene request, and stats. Not game
    /// content production — just enough for deterministic parity proofs.
    ///
    /// Layout:
    ///   Phase 0 "Warm Up"  [warm]   target 2   → cards Warm One / Warm Two
    ///   Phase 1 "Choice"   [choice] target 1   → card Crossroads
    ///   Phase 2 "Ending"   [ending] target 1   → card The End (cutscene)
    /// </summary>
    public static class ParityFixture
    {
        public static SessionDefinition MakeSession()
        {
            var session = new SessionDefinition { Title = "Parity Session", Tags = { "sfw" } };
            session.Phases.Add(new PhaseDefinition { Title = "Warm Up", MinCards = 2, MaxCards = 2, MustIncludeTags = { "warm" } });
            session.Phases.Add(new PhaseDefinition { Title = "Choice", MinCards = 1, MaxCards = 1, MustIncludeTags = { "choice" } });
            session.Phases.Add(new PhaseDefinition { Title = "Ending", MinCards = 1, MaxCards = 1, MustIncludeTags = { "ending" } });
            return session;
        }

        public static CardDeckDefinition MakeDeck()
        {
            var warmOne = new CardDefinition { Title = "Warm One", Tags = { "warm" } };
            warmOne.Actions.Add(new StatIncreaseActionDefinition { StatKey = "courage", Amount = 2, IsBlocking = true });
            warmOne.Actions.Add(new DebugActionDefinition { Message = "warming up", DelaySeconds = 0.5f, IsBlocking = false });

            var warmTwo = new CardDefinition { Title = "Warm Two", Tags = { "warm" } };
            warmTwo.Actions.Add(new DebugActionDefinition { Message = "again", IsBlocking = true });

            var crossroads = new CardDefinition { Title = "Crossroads", Tags = { "choice" } };
            crossroads.Actions.Add(new ChoiceActionDefinition
            {
                Prompt = "Brave or cautious?",
                IsBlocking = true,
                Options =
                {
                    new ChoiceOptionDefinition
                    {
                        Label = "Brave",
                        Child = new StatIncreaseActionDefinition { StatKey = "brave", Amount = 5, IsBlocking = true }
                    },
                    new ChoiceOptionDefinition { Label = "Cautious", Child = null }
                }
            });

            var theEnd = new CardDefinition { Title = "The End", Tags = { "ending" } };
            theEnd.Actions.Add(new CutsceneActionDefinition { ResourceId = "cs:finale", IsBlocking = true });

            return new CardDeckDefinition { Cards = { warmOne, warmTwo, crossroads, theEnd } };
        }
    }
}
