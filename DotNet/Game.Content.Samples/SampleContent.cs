using System.Collections.Generic;

namespace TruthCardGame.Content.Samples
{
    /// <summary>
    /// SFW sample content faithful to the Unity sample assets
    /// (Assets/Content/*): identical stable IDs, titles, tags and structure.
    /// It is the code-built source used to seed the canonical SQLite database
    /// and to drive tests and the reference host, replacing the old JSON
    /// fixture.
    ///
    /// Transitional PhaseSlot/candidate IDs follow the Ticket-01 rule:
    ///   slot:      legacy-slot:&lt;sessionId&gt;:&lt;phaseId&gt;:&lt;occurrence&gt;
    ///   candidate: legacy-candidate:&lt;slotId&gt;:&lt;index&gt;
    /// so regenerating the snapshot from Unity never churns IDs.
    /// </summary>
    public static class SampleContent
    {
        // Phase IDs (Phase_*.asset)
        private const string PhaseWarmUp = "7cccee8c09144016b3954e83e3493365";
        private const string PhaseTeasing = "1122d60cc029403e8247db97ebdbb05a";
        private const string PhaseBuild = "8965075142604a72a36593c6ed437788";
        private const string PhaseHighIntensity = "a457b7d236654690bedbfcfd696db390";
        private const string PhaseWindDown = "8eff598d26324917b3a256e434f165de";
        private const string PhaseTheEnd = "2d7417bc6057427395b62c76a1fb718e";

        // Session IDs (Intense.asset, Relaxing.asset)
        private const string SessionIntense = "9ac9fbe9ced7463a8627356e21644096";
        private const string SessionRelaxing = "8741477f677e443f828f87267c32d537";

        // Action IDs
        private const string ActionCouragePlusOne = "d0b7a66bba314acb94c6133d22d6a732";
        private const string ActionDebugBlocking = "9716474d22de4236bfdb45c7a953b39a";
        private const string ActionDebugContinuous = "3d0a65f2c63147568656c4f95e8ff774";
        private const string ActionCutsceneIntro = "9bc0a33140c345bb92da5a43c62869c5";
        private const string ActionChoiceFaceTheCrowd = "1625604f324e456f916807c1d00fc97f";

        // Card IDs
        private const string CardCourageBoost = "cb5e05de6a3b437c950f25fbd42c1ff3";
        private const string CardTheCrowdWatches = "b6839ac247c642ab86a78e86a68a1bee";
        private const string CardAmbientWhispers = "29e2b4399a0d4d57861cd481cb42c683";
        private const string CardDareAndCelebrate = "3f7191b1557a4907bb53fa8ff1788aa3";
        private const string CardTwinWhispers = "d00dcc10e12c4e71a131b45ef3df0e19";
        private const string CardCutsceneIntro = "54e39eb4014f4b73b017a9315b130c21";
        private const string CardFaceTheCrowd = "5fea312b53554d3f80428480c3c2cf3b";

        // Resource ID
        private const string ResourceCutsceneIntro = "cs:intro";

        public static GameContentDefinition Build()
        {
            var content = new GameContentDefinition
            {
                Deck = new CardDeckDefinition
                {
                    Id = "b8399104a3ee4bd7b0b61771d76d0e0f",
                    Title = "StarterDeck",
                    CardIds =
                    {
                        CardCourageBoost,
                        CardTheCrowdWatches,
                        CardAmbientWhispers,
                        CardDareAndCelebrate,
                        CardTwinWhispers,
                        CardCutsceneIntro,
                        CardFaceTheCrowd
                    }
                }
            };

            content.Phases.AddRange(new[]
            {
                new PhaseDefinition { Id = PhaseWarmUp, Title = "Warm Up", MinCards = 2, MaxCards = 3, MustIncludeTags = { "solo" } },
                new PhaseDefinition { Id = PhaseTeasing, Title = "Teasing", MinCards = 3, MaxCards = 4, MustIncludeTags = { "solo", "truth" } },
                new PhaseDefinition { Id = PhaseBuild, Title = "Build", MinCards = 3, MaxCards = 4, MustIncludeTags = { "party" } },
                new PhaseDefinition { Id = PhaseHighIntensity, Title = "High Intensity", MinCards = 4, MaxCards = 5, MustIncludeTags = { "party", "dare" } },
                new PhaseDefinition { Id = PhaseWindDown, Title = "Wind Down", MinCards = 1, MaxCards = 2, MustIncludeTags = { "ending" } },
                new PhaseDefinition { Id = PhaseTheEnd, Title = "The End", MinCards = 1, MaxCards = 2, MustIncludeTags = { "ending" } }
            });

            content.Actions.AddRange(new GameActionDefinition[]
            {
                new StatIncreaseActionDefinition
                {
                    Id = ActionCouragePlusOne,
                    Name = "CouragePlusOne",
                    StatKey = "courage",
                    Amount = 1,
                    IsBlocking = true
                },
                new DebugActionDefinition
                {
                    Id = ActionDebugBlocking,
                    Name = "Debug_Blocking",
                    Message = "A hush falls over the room. Everyone is watching you\u2026",
                    DelaySeconds = 2.5f,
                    IsBlocking = true
                },
                new DebugActionDefinition
                {
                    Id = ActionDebugContinuous,
                    Name = "Debug_Continuous",
                    Message = "A low murmur of whispers carries across the room\u2026",
                    DelaySeconds = 5f,
                    IsBlocking = false
                },
                new CutsceneActionDefinition
                {
                    Id = ActionCutsceneIntro,
                    Name = "Cutscene_Intro",
                    ResourceId = ResourceCutsceneIntro,
                    IsBlocking = true
                },
                new ChoiceActionDefinition
                {
                    Id = ActionChoiceFaceTheCrowd,
                    Name = "Choice_FaceTheCrowd",
                    Prompt = "The crowd leans in. How do you answer?",
                    IsBlocking = true,
                    Options =
                    {
                        new ChoiceOptionDefinition
                        {
                            Id = "e7d944eb749d4bdf8ae69383de600abf",
                            Label = "Own it",
                            ChildActionId = ActionCouragePlusOne
                        },
                        new ChoiceOptionDefinition
                        {
                            Id = "c88838e4dcf248e1ba9e46de9f30ec5a",
                            Label = "Shrug it off",
                            ChildActionId = ActionDebugContinuous
                        }
                    }
                }
            });

            content.Cards.AddRange(new[]
            {
                new CardDefinition { Id = CardCourageBoost, Title = "Courage Boost", Tags = { "party", "truth" }, ActionIds = { ActionCouragePlusOne } },
                new CardDefinition { Id = CardTheCrowdWatches, Title = "The Crowd Watches", Tags = { "party", "dare" }, ActionIds = { ActionDebugBlocking } },
                new CardDefinition { Id = CardAmbientWhispers, Title = "Ambient Whispers", Tags = { "solo", "truth" }, ActionIds = { ActionDebugContinuous } },
                new CardDefinition { Id = CardDareAndCelebrate, Title = "Dare & Celebrate", Tags = { "party", "dare" }, ActionIds = { ActionCouragePlusOne, ActionDebugBlocking } },
                new CardDefinition { Id = CardTwinWhispers, Title = "Twin Whispers", Tags = { "solo" }, ActionIds = { ActionDebugContinuous, ActionDebugContinuous } },
                new CardDefinition { Id = CardCutsceneIntro, Title = "A Familiar Face", Tags = { "cutscene" }, ActionIds = { ActionCutsceneIntro } },
                new CardDefinition { Id = CardFaceTheCrowd, Title = "Face the Crowd", Tags = { "party", "dare" }, ActionIds = { ActionChoiceFaceTheCrowd } }
            });

            content.Resources.Add(new ResourceDefinition
            {
                Id = ResourceCutsceneIntro,
                Kind = "cutscene",
                Name = "Intro"
            });

            content.Sessions.AddRange(new[]
            {
                MakeSession(SessionIntense, "Intense", new[] { "intense" },
                    ("Build", PhaseBuild),
                    ("High Intensity", PhaseHighIntensity),
                    ("The End", PhaseTheEnd)),
                MakeSession(SessionRelaxing, "Relaxing", new[] { "relaxing" },
                    ("Warm Up", PhaseWarmUp),
                    ("Teasing", PhaseTeasing),
                    ("Wind Down", PhaseWindDown))
            });

            return content;
        }

        private static SessionDefinition MakeSession(
            string sessionId,
            string title,
            IEnumerable<string> tags,
            params (string SlotTitle, string PhaseId)[] slots)
        {
            var session = new SessionDefinition { Id = sessionId, Title = title };
            session.Tags.AddRange(tags);

            var occurrence = 0;
            foreach (var (slotTitle, phaseId) in slots)
            {
                var slotId = $"legacy-slot:{sessionId}:{phaseId}:{occurrence}";
                var slot = new PhaseSlotDefinition { Id = slotId, Title = slotTitle };
                slot.Candidates.Add(new PhaseSlotCandidateDefinition
                {
                    Id = $"legacy-candidate:{slotId}:0",
                    PhaseId = phaseId
                });
                session.PhaseSlots.Add(slot);
                occurrence++;
            }

            return session;
        }
    }
}
