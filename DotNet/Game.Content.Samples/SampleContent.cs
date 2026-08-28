using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Samples
{
    /// <summary>
    /// Code-built snapshot of the Unity sample content, reshaped to the
    /// Milestone B model (Docs/MilestoneB/02-schema-audit.md).
    ///
    /// - Authored stable IDs and titles stay faithful to Assets/Content.
    /// - Card tags are dedicated CardTagDefinition rows with stable IDs;
    ///   phases carry include-only ALL/ANY Card queries (no exclusion).
    /// - Every Phase carries the standard executable graph: Entry →
    ///   CardExecutor → happiness-fail check → progress-complete check,
    ///   looping back to the CardExecutor until an exit fires. The terminal
    ///   "The End" phase omits the fail path and exports only Complete.
    /// - Sessions wire projected reference sockets per contract: Complete →
    ///   next reference (last one: SessionEnd), Fail → SessionEnd directly.
    ///
    /// Tests, seeds, and hosts all build content from here.
    /// </summary>
    public static class SampleContent
    {
        // ---- stable ids kept faithful to the Unity sample assets ----

        public const string TypeStandard = "type-standard";
        public const string TemperatureHappiness = "happiness";

        public const string ResourceCutsceneIntro = "res-cutscene-intro-a-familiar-face";

        // Card tag definitions (v5): stable catalog ids, faithful titles.
        public const string CardTagCutscene = "cardtag-cutscene";
        public const string CardTagParty = "cardtag-party";
        public const string CardTagTruth = "cardtag-truth";
        public const string CardTagSolo = "cardtag-solo";
        public const string CardTagDare = "cardtag-dare";
        public const string CardTagEnding = "cardtag-ending";

        public const string SessionIntense = "9ac9fbe9ced7463a8627356e21644096";   // Intense
        public const string SessionRelaxing = "8741477f677e443f828f87267c32d537";  // Relaxing

        public const string PhaseWarmUp = "7cccee8c09144016b3954e83e3493365";         // Warm Up
        public const string PhaseBuild = "8965075142604a72a36593c6ed437788";          // Build
        public const string PhaseHighIntensity = "a457b7d236654690bedbfcfd696db390";  // High Intensity
        public const string PhaseTeasing = "1122d60cc029403e8247db97ebdbb05a";        // Teasing
        public const string PhaseWindDown = "8eff598d26324917b3a256e434f165de";       // Wind Down
        public const string PhaseTheEndPhase = "2d7417bc6057427395b62c76a1fb718e";    // The End (phase)

        public const string CardCutsceneIntro = "54e39eb4014f4b73b017a9315b130c21";  // A Familiar Face
        public const string CardCourageBoost = "cb5e05de6a3b437c950f25fbd42c1ff3";   // Courage Boost
        public const string CardAmbientWhispers = "29e2b4399a0d4d57861cd481cb42c683";// Ambient Whispers
        public const string CardTwinWhispers = "d00dcc10e12c4e71a131b45ef3df0e19";   // Twin Whispers
        public const string CardFaceTheCrowd = "5fea312b53554d3f80428480c3c2cf3b";   // Face the Crowd
        public const string CardTheCrowdWatches = "b6839ac247c642ab86a78e86a68a1bee";// The Crowd Watches
        public const string CardDareAndCelebrate = "3f7191b1557a4907bb53fa8ff1788aa3"; // Dare & Celebrate
        public const string CardTheEnd = "45f4fe6e49b74397adee8f44f2a8c0fe";         // The End (card)

        private const float ProgressCompleteAt = 100f;
        private const float HappinessFailBelow = 10f;

        /// <summary>Builds a fresh snapshot; every call returns new object graphs safe to mutate.</summary>
        public static GameContentDefinition Create()
        {
            var content = new GameContentDefinition
            {
                SessionTypes = { new SessionTypeDefinition { Id = TypeStandard, Title = "Standard" } },
                Temperatures =
                {
                    new TemperatureDefinition
                    {
                        Id = TemperatureHappiness,
                        Title = "Happiness",
                        MinValue = 0f,
                        MaxValue = 100f,
                        DefaultValue = 50f,
                    },
                },
                CardTagDefinitions =
                {
                    new CardTagDefinition { Id = CardTagCutscene, Title = "Cutscene", SortOrder = 0 },
                    new CardTagDefinition { Id = CardTagParty, Title = "Party", SortOrder = 1 },
                    new CardTagDefinition { Id = CardTagTruth, Title = "Truth", SortOrder = 2 },
                    new CardTagDefinition { Id = CardTagSolo, Title = "Solo", SortOrder = 3 },
                    new CardTagDefinition { Id = CardTagDare, Title = "Dare", SortOrder = 4 },
                    new CardTagDefinition { Id = CardTagEnding, Title = "Ending", SortOrder = 5 },
                },
                Resources = { new ResourceDefinition { Id = ResourceCutsceneIntro, Name = "A Familiar Face" } },
            };

            content.Cards.Add(BuildCardAEncouragingStart());
            content.Cards.Add(BuildCardIntroduction());
            content.Cards.Add(BuildCardQuietMoment());
            content.Cards.Add(BuildCardTwoVoices());
            content.Cards.Add(BuildCardCenterStage());
            content.Cards.Add(BuildCardWatchfulEyes());
            content.Cards.Add(BuildCardGrandFinale());
            content.Cards.Add(BuildCardFinalTally());

            content.Phases.Add(StandardPhase(PhaseWarmUp, "Warm Up"));
            content.Phases.Add(StandardPhase(PhaseBuild, "Build"));
            content.Phases.Add(StandardPhase(PhaseHighIntensity, "High Intensity", allTags: TagList(CardTagParty)));
            content.Phases.Add(StandardPhase(PhaseTeasing, "Teasing"));
            content.Phases.Add(StandardPhase(PhaseWindDown, "Wind Down"));
            content.Phases.Add(TerminalPhase(PhaseTheEndPhase, "The End", allTags: TagList(CardTagEnding)));

            var phaseIndex = new Dictionary<string, PhaseDefinition>();
            foreach (var phase in content.Phases)
            {
                phaseIndex[phase.Id] = phase;
            }

            content.Sessions.Add(BuildSession(SessionIntense, "Intense",
                phaseIndex[PhaseWarmUp], phaseIndex[PhaseBuild], phaseIndex[PhaseHighIntensity]));
            content.Sessions.Add(BuildSession(SessionRelaxing, "Relaxing",
                phaseIndex[PhaseWarmUp], phaseIndex[PhaseTeasing], phaseIndex[PhaseWindDown],
                phaseIndex[PhaseTheEndPhase]));

            return content;
        }

        private static List<string> TagList(params string[] values)
        {
            return new List<string>(values);
        }

        // ---- session composition ----

        /// <summary>
        /// Builds Start → [refs…] → End where every Complete socket continues to the
        /// next reference (the last completes into SessionEnd) and every Fail socket
        /// jumps straight to SessionEnd. Only exits actually exported by the
        /// referenced phase receive sockets.
        /// </summary>
        private static SessionDefinition BuildSession(string id, string title, params PhaseDefinition[] phases)
        {
            var start = new SessionStartNodeDefinition { Id = $"n-{id}-start" };
            start.Outputs.Add(NormalOut(start.Id));

            var end = new SessionEndNodeDefinition { Id = $"n-{id}-end" };

            var references = new List<PhaseReferenceNodeDefinition>();
            var failSockets = new HashSet<string>(); // output ids projecting a non-complete exit
            foreach (var phase in phases)
            {
                var node = new PhaseReferenceNodeDefinition { Id = $"n-{id}-ref-{phase.Id}", PhaseId = phase.Id };
                foreach (var exit in phase.Exits)
                {
                    var socketId = $"{node.Id}-socket-{exit.Name.ToLowerInvariant().Replace(' ', '-')}";
                    node.Outputs.Add(new GraphOutputDefinition
                    {
                        Id = socketId,
                        Kind = GraphPortKind.PhaseExit,
                        PhaseExitId = exit.Id,
                    });
                    if (exit.Name != "Complete")
                    {
                        failSockets.Add(socketId);
                    }
                }
                references.Add(node);
            }

            var edges = new List<GraphEdgeDefinition>();
            edges.Add(new GraphEdgeDefinition
            {
                Id = $"pe-{id}-start",
                SourceOutputId = start.Outputs[0].Id,
                TargetNodeId = references.Count > 0 ? references[0].Id : end.Id,
            });

            for (var i = 0; i < references.Count; i++)
            {
                var current = references[i];
                foreach (var output in current.Outputs)
                {
                    var targetNodeId = failSockets.Contains(output.Id) || i == references.Count - 1
                        ? end.Id
                        : references[i + 1].Id;

                    edges.Add(new GraphEdgeDefinition
                    {
                        Id = $"pe-{id}-{i}-{output.Kind}-{edges.Count}",
                        SourceOutputId = output.Id,
                        TargetNodeId = targetNodeId,
                    });
                }
            }

            var nodes = new List<SessionGraphNodeDefinition> { start, end };
            nodes.AddRange(references);

            return new SessionDefinition
            {
                Id = id,
                Title = title,
                SessionTypeId = TypeStandard,
                Graph = new SessionGraphDefinition
                {
                    Nodes = nodes,
                    Edges = edges,
                },
            };
        }

        // ---- phase graphs ----

        /// <summary>The standard two-exit low-level graph. Fail check feeds a GOTO Fail action node; progress check loops or completes.</summary>
        private static PhaseDefinition StandardPhase(string id, string title, List<string> allTags = null)
        {
            return PhaseGraph(id, title, allTags, withFailPath: true);
        }

        /// <summary>Terminal variant without a fail exit/check.</summary>
        private static PhaseDefinition TerminalPhase(string id, string title, List<string> allTags = null)
        {
            return PhaseGraph(id, title, allTags, withFailPath: false);
        }

        private static PhaseDefinition PhaseGraph(string id, string title, List<string> allTags, bool withFailPath)
        {
            var entry = NormalOut(new PhaseEntryNodeDefinition { Id = $"pn-{id}-entry" });
            var draw = NormalOut(new CardExecutorNodeDefinition { Id = $"pn-{id}-draw" });

            VariableCheckNodeDefinition failCheck = null;
            ActionNodeDefinition gotoFail = null;
            if (withFailPath)
            {
                failCheck = new VariableCheckNodeDefinition
                {
                    Id = $"pn-{id}-failcheck",
                    SourceKind = VariableSourceKind.Temperature,
                    VariableKey = TemperatureHappiness,
                    Operator = VariableCompareOperator.LessThan,
                    CompareValue = HappinessFailBelow,
                };
                failCheck.Outputs.Add(TrueOut(failCheck.Id));
                failCheck.Outputs.Add(FalseOut(failCheck.Id));

                gotoFail = GotoNode($"pan-{id}-goto-fail", id, complete: false);
            }

            var doneCheck = new VariableCheckNodeDefinition
            {
                Id = $"pn-{id}-donecheck",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = ProgressCompleteAt,
            };
            doneCheck.Outputs.Add(TrueOut(doneCheck.Id));
            doneCheck.Outputs.Add(FalseOut(doneCheck.Id));

            var gotoDone = GotoNode($"pan-{id}-goto-done", id, complete: true);

            // Wire the standard template:
            //   Entry -> Draw -> [fail check -> GOTO Fail | Done-check] ...
            //   Done-check True -> GOTO Complete; False -> Draw (user-paced loop).
            var nodes = new List<GraphNodeDefinition> { entry, draw };
            var edges = new List<GraphEdgeDefinition>();

            Edge(edges, entry.Outputs[0].Id, draw.Id);
            if (withFailPath)
            {
                nodes.Add(failCheck);
                nodes.Add(gotoFail);
                Edge(edges, draw.Outputs[0].Id, failCheck.Id);
                Edge(edges, FindOutput(failCheck, GraphPortKind.True).Id, gotoFail.Id);
                Edge(edges, FindOutput(failCheck, GraphPortKind.False).Id, doneCheck.Id);
            }
            else
            {
                Edge(edges, draw.Outputs[0].Id, doneCheck.Id);
            }
            nodes.Add(doneCheck);
            nodes.Add(gotoDone);
            Edge(edges, FindOutput(doneCheck, GraphPortKind.True).Id, gotoDone.Id);
            Edge(edges, FindOutput(doneCheck, GraphPortKind.False).Id, draw.Id); // loop until progress completes

            var exits = new List<PhaseExitDefinition>
            {
                new PhaseExitDefinition { Id = ExitId(id, complete: true), Name = "Complete" },
            };
            if (withFailPath)
            {
                exits.Add(new PhaseExitDefinition { Id = ExitId(id, complete: false), Name = "Fail" });
            }

            return new PhaseDefinition
            {
                Id = id,
                Title = title,
                MustHaveAllCardTags = AddAll(allTags),
                Exits = exits,
                Graph = new PhaseGraphDefinition
                {
                    Nodes = nodes,
                    Edges = edges,
                },
            };
        }

        private static List<string> AddAll(List<string> optional)
        {
            if (optional == null)
            {
                return new List<string>();
            }
            return new List<string>(optional);
        }

        private static string ExitId(string phaseId, bool complete)
        {
            return complete ? $"px-{phaseId}-complete" : $"px-{phaseId}-fail";
        }

        private static ActionNodeDefinition GotoNode(string nodeId, string phaseId, bool complete)
        {
            var node = new ActionNodeDefinition
            {
                Id = nodeId,
                Sequence = new ActionSequenceDefinition
                {
                    Id = $"{nodeId}-seq",
                    Instances =
                    {
                        new PhaseGotoInstanceDefinition
                        {
                            Id = $"inst-{phaseId}-goto-{(complete ? "done" : "fail")}",
                            PhaseExitId = ExitId(phaseId, complete),
                        },
                    },
                },
            };
            node.Outputs.Add(NormalOut(node.Id));
            return node;
        }

        private static T NormalOut<T>(T node) where T : GraphNodeDefinition
        {
            node.Outputs.Add(NormalOut(node.Id));
            return node;
        }

        private static GraphOutputDefinition NormalOut(string id)
        {
            return Out(id, GraphPortKind.Normal);
        }

        private static GraphOutputDefinition TrueOut(string nodeId)
        {
            return Out(nodeId + "-true", GraphPortKind.True);
        }

        private static GraphOutputDefinition FalseOut(string nodeId)
        {
            return Out(nodeId + "-false", GraphPortKind.False);
        }

        private static GraphOutputDefinition Out(string id, GraphPortKind kind)
        {
            return new GraphOutputDefinition { Id = id, Kind = kind };
        }

        private static void Edge(List<GraphEdgeDefinition> edges, string sourceOutputId, string targetNodeId)
        {
            edges.Add(new GraphEdgeDefinition
            {
                Id = $"edge-{sourceOutputId}->{targetNodeId}",
                SourceOutputId = sourceOutputId,
                TargetNodeId = targetNodeId,
            });
        }

        private static GraphOutputDefinition FindOutput(GraphNodeDefinition node, GraphPortKind kind)
        {
            foreach (var output in node.Outputs)
            {
                if (output.Kind == kind)
                {
                    return output;
                }
            }
            throw new KeyNotFoundException($"no '{kind}' output on node '{node.Id}'.");
        }

        // ---- cards ----

        private static ActionSequenceDefinition Seq(params ActionInstanceDefinition[] instances)
        {
            var sequence = new ActionSequenceDefinition { Id = "seq-" + instances[0].Id };
            foreach (var instance in instances)
            {
                sequence.Instances.Add(instance);
            }
            return sequence;
        }

        private static IncrementProgressInstanceDefinition Progress(string instanceId, float amount = 10f)
        {
            return new IncrementProgressInstanceDefinition { Id = instanceId, Amount = amount };
        }

        private static WaitForContinueInstanceDefinition Wait(string instanceId)
        {
            return new WaitForContinueInstanceDefinition { Id = instanceId, IsBlocking = true };
        }

        private static PromptChoiceOptionDefinition Option(string optionId, string label, params ActionInstanceDefinition[] instances)
        {
            var sequence = new ActionSequenceDefinition { Id = "seq-" + optionId };
            foreach (var instance in instances)
            {
                sequence.Instances.Add(instance);
            }
            return new PromptChoiceOptionDefinition { Id = optionId, Label = label, Sequence = sequence };
        }

        private static CardDefinition BuildCardIntroduction()
        {
            return new CardDefinition
            {
                Id = CardCutsceneIntro,
                Title = "A Familiar Face",
                CardTagIds = { CardTagCutscene },
                Sequence = Seq(
                    Wait("inst-intro-wait"),
                    new CutsceneInstanceDefinition { Id = "inst-intro-cutscene", ResourceId = ResourceCutsceneIntro },
                    Progress("inst-intro-progress")),
            };
        }

        private static CardDefinition BuildCardAEncouragingStart()
        {
            return new CardDefinition
            {
                Id = CardCourageBoost,
                Title = "Courage Boost",
                CardTagIds = { CardTagParty, CardTagTruth },
                Sequence = Seq(
                    Wait("inst-courage-wait"),
                    new StatIncreaseInstanceDefinition { Id = "inst-courage-up", StatKey = "courage", Amount = 1f },
                    Progress("inst-courage-progress")),
            };
        }

        private static CardDefinition BuildCardQuietMoment()
        {
            return new CardDefinition
            {
                Id = CardAmbientWhispers,
                Title = "Ambient Whispers",
                CardTagIds = { CardTagSolo, CardTagTruth },
                Sequence = Seq(
                    Wait("inst-ambient-wait"),
                    new DebugInstanceDefinition { Id = "inst-ambient-whisper", Message = "A soft whisper at the edge of hearing." },
                    Progress("inst-ambient-progress")),
            };
        }

        private static CardDefinition BuildCardTwoVoices()
        {
            return new CardDefinition
            {
                Id = CardTwinWhispers,
                Title = "Twin Whispers",
                CardTagIds = { CardTagSolo },
                Sequence = Seq(
                    Wait("inst-twin-wait"),
                    new PromptChoiceInstanceDefinition
                    {
                        Id = "inst-twin-choice",
                        Prompt = "A voice whispers twin words, one to each ear.",
                        Options =
                        {
                            Option("opt-twin-left", "Listen left",
                                new DebugInstanceDefinition { Id = "inst-twin-left", Message = "The left voice repeats your own name back to you." }),
                            Option("opt-twin-right", "Listen right",
                                new DebugInstanceDefinition
                                {
                                    Id = "inst-twin-right",
                                    Message = "The right voice keeps murmuring even when you stop listening.",
                                    IsBlocking = false,
                                }),
                        },
                    },
                    Progress("inst-twin-progress")),
            };
        }

        private static CardDefinition BuildCardCenterStage()
        {
            return new CardDefinition
            {
                Id = CardFaceTheCrowd,
                Title = "Face the Crowd",
                CardTagIds = { CardTagParty, CardTagDare },
                Sequence = Seq(
                    Wait("inst-crowdcard-wait"),
                    new PromptChoiceInstanceDefinition
                    {
                        Id = "inst-crowd-choice",
                        Prompt = "The crowd dares you to hold its gaze.",
                        Options =
                        {
                            Option("opt-crowd-face", "Face it",
                                new StatIncreaseInstanceDefinition { Id = "inst-crowd-courage", StatKey = "courage", Amount = 2f },
                                Progress("inst-crowd-face-progress", 15f)),
                            Option("opt-crowd-shrug", "Shrug it off",
                                new DebugInstanceDefinition
                                {
                                    Id = "inst-crowd-shrug-murmur",
                                    Message = "You shrug; somewhere behind you the murmur never fully stops.",
                                    IsBlocking = false,
                                }),
                        },
                    },
                    Progress("inst-crowdcard-progress")),
            };
        }

        private static CardDefinition BuildCardWatchfulEyes()
        {
            return new CardDefinition
            {
                Id = CardTheCrowdWatches,
                Title = "The Crowd Watches",
                CardTagIds = { CardTagParty, CardTagDare },
                Sequence = Seq(
                    Wait("inst-watchful-wait"),
                    new DebugInstanceDefinition { Id = "inst-watchful-jeer", Message = "The crowd leans in; nobody speaks first.", IsBlocking = false },
                    Progress("inst-watchful-progress")),
            };
        }

        private static CardDefinition BuildCardGrandFinale()
        {
            return new CardDefinition
            {
                Id = CardDareAndCelebrate,
                Title = "Dare & Celebrate",
                CardTagIds = { CardTagParty, CardTagDare },
                Sequence = Seq(
                    Wait("inst-dare-wait"),
                    new StatIncreaseInstanceDefinition { Id = "inst-dare-spark", StatKey = "spark", Amount = 3f },
                    new DebugInstanceDefinition { Id = "inst-dare-log", Message = "The dare lands; cheers follow." },
                    Progress("inst-dare-progress")),
            };
        }

        private static CardDefinition BuildCardFinalTally()
        {
            return new CardDefinition
            {
                Id = CardTheEnd,
                Title = "The End",
                CardTagIds = { CardTagEnding },
                Sequence = Seq(
                    Wait("inst-theend-wait"),
                    // Ends the whole session the moment this card executes.
                    new EndSessionInstanceDefinition { Id = "inst-end-now" },
                    // Deliberately unreachable after EndSession; authors may move or delete such trailing actions.
                    Progress("inst-theend-progress")),
            };
        }
    }
}
