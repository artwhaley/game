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
    ///   Slot 0 "Warm Up"  -> Phase [warm]   target 2   → cards Warm One / Warm Two
    ///   Slot 1 "Choice"   -> Phase [choice] target 1   → card Crossroads
    ///   Slot 2 "Ending"   -> Phase [ending] target 1   → card The End (cutscene)
    /// </summary>
    public static class ParityFixture
    {
        public const string SessionId = "session-parity";

        public static GameContentDefinition MakeContent()
        {
            var warmUp = new PhaseDefinition { Id = "phase-warm-up", Title = "Warm Up", MinCards = 2, MaxCards = 2, MustIncludeTags = { "warm" } };
            var choicePhase = new PhaseDefinition { Id = "phase-choice", Title = "Choice", MinCards = 1, MaxCards = 1, MustIncludeTags = { "choice" } };
            var ending = new PhaseDefinition { Id = "phase-ending", Title = "Ending", MinCards = 1, MaxCards = 1, MustIncludeTags = { "ending" } };

            var courage = new StatIncreaseActionDefinition { Id = "action-courage", StatKey = "courage", Amount = 2, IsBlocking = true };
            var warmDebug = new DebugActionDefinition { Id = "action-warm-debug", Message = "warming up", DelaySeconds = 0.5f, IsBlocking = false };
            var again = new DebugActionDefinition { Id = "action-again", Message = "again", IsBlocking = true };
            var brave = new StatIncreaseActionDefinition { Id = "action-brave", StatKey = "brave", Amount = 5, IsBlocking = true };
            var crossroadsChoice = new ChoiceActionDefinition
            {
                Id = "action-choice",
                Prompt = "Brave or cautious?",
                IsBlocking = true,
                Options =
                {
                    new ChoiceOptionDefinition { Id = "opt-brave", Label = "Brave", ChildActionId = brave.Id },
                    new ChoiceOptionDefinition { Id = "opt-cautious", Label = "Cautious" }
                }
            };
            var finale = new CutsceneActionDefinition { Id = "action-cutscene", ResourceId = "cs:finale", IsBlocking = true };

            var warmOne = new CardDefinition { Id = "card-warm-one", Title = "Warm One", Tags = { "warm" }, ActionIds = { courage.Id, warmDebug.Id } };
            var warmTwo = new CardDefinition { Id = "card-warm-two", Title = "Warm Two", Tags = { "warm" }, ActionIds = { again.Id } };
            var crossroads = new CardDefinition { Id = "card-crossroads", Title = "Crossroads", Tags = { "choice" }, ActionIds = { crossroadsChoice.Id } };
            var theEnd = new CardDefinition { Id = "card-the-end", Title = "The End", Tags = { "ending" }, ActionIds = { finale.Id } };

            var session = new SessionDefinition { Id = SessionId, Title = "Parity Session", Tags = { "sfw" } };
            session.PhaseSlots.Add(Slot("slot-0", "Warm Up", warmUp.Id));
            session.PhaseSlots.Add(Slot("slot-1", "Choice", choicePhase.Id));
            session.PhaseSlots.Add(Slot("slot-2", "Ending", ending.Id));

            return new GameContentDefinition
            {
                Deck = new CardDeckDefinition { Id = "deck-parity", CardIds = { warmOne.Id, warmTwo.Id, crossroads.Id, theEnd.Id } },
                Sessions = { session },
                Phases = { warmUp, choicePhase, ending },
                Cards = { warmOne, warmTwo, crossroads, theEnd },
                Actions = { courage, warmDebug, again, brave, crossroadsChoice, finale },
                Resources = { new ResourceDefinition { Id = "cs:finale", Kind = "cutscene", Name = "Finale" } }
            };
        }

        private static PhaseSlotDefinition Slot(string id, string title, string phaseId)
        {
            var slot = new PhaseSlotDefinition { Id = id, Title = title };
            slot.Candidates.Add(new PhaseSlotCandidateDefinition { Id = id + ":candidate", PhaseId = phaseId });
            return slot;
        }
    }
}
