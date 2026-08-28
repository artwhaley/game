using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 02 gate tests: the v2 graph + Action-Instance model must represent
    /// the actual domain without any Unity/WPF dependency and without the deleted
    /// PhaseSlot/reusable-action concepts.
    /// </summary>
    [TestFixture]
    public class ContentModelTests
    {
        private static GameContentDefinition Sample() => SampleContent.Create();

        [Test]
        public void SamePhase_CanBeReferencedByMultipleSessions()
        {
            var content = Sample();
            var warmUpId = content.Phases.Single(p => p.Title == "Warm Up").Id;

            var referencingPlacements = content.Sessions
                .SelectMany(s => s.Graph.Nodes)
                .OfType<PhaseReferenceNodeDefinition>()
                .Where(node => node.PhaseId == warmUpId)
                .ToList();

            Assert.GreaterOrEqual(referencingPlacements.Count, 2,
                "the Warm Up phase should be placed by more than one session");
        }

        [Test]
        public void ActionInstances_AreOwnedCopies_NotSharedEntities()
        {
            var content = Sample();

            // The two choice cards each own their own PromptChoice occurrence object.
            var twinChoice = content.Cards
                .Single(c => c.Id == SampleContent.CardTwinWhispers)
                .Sequence.Instances.OfType<PromptChoiceInstanceDefinition>().Single();
            var crowdChoice = content.Cards
                .Single(c => c.Id == SampleContent.CardFaceTheCrowd)
                .Sequence.Instances.OfType<PromptChoiceInstanceDefinition>().Single();

            Assert.AreNotSame(twinChoice, crowdChoice);
            Assert.AreEqual("inst-twin-choice", twinChoice.Id);
            Assert.AreEqual("inst-crowd-choice", crowdChoice.Id);

            Assert.That(AllInstanceIds(content), Is.Unique,
                "occurrence IDs must be unique across the whole snapshot; instances are never shared");
        }

        [Test]
        public void SameActionType_CanHaveDifferentValues_InTwoInstances()
        {
            var progressInstances = AllInstances(Sample())
                .OfType<IncrementProgressInstanceDefinition>()
                .ToList();

            var tenners = progressInstances.Where(p => p.Amount == 10f).ToList();
            var fifteens = progressInstances.Where(p => p.Amount == 15f).ToList();
            Assert.GreaterOrEqual(tenners.Count, 5);
            Assert.AreEqual(1, fifteens.Count,
                "Face-the-Crowd's Face-it option progresses +15 while every default is +10");

            // Mutating one instance must never leak into another of the same type.
            var victim = tenners.First();
            victim.Amount = 42f;
            foreach (var other in progressInstances.Where(p => p != victim))
            {
                Assert.AreNotEqual(42f, other.Amount);
            }
        }

        [Test]
        public void PhaseExit_StableId_IsIndependentOfDisplayName()
        {
            var phase = Sample().Phases.Single(p => p.Id == SampleContent.PhaseWarmUp);
            var completeExit = phase.Exits.Single(e => e.Name == "Complete");

            var idBeforeRename = completeExit.Id;
            completeExit.Name = "Completed All The Way";

            Assert.AreEqual(idBeforeRename, completeExit.Id);

            // GOTO instances keep wiring to the renamed exit because they store IDs.
            var gotoTargets = phase.Graph.Nodes
                .OfType<ActionNodeDefinition>()
                .SelectMany(n => n.Sequence.Instances)
                .Cast<PhaseGotoInstanceDefinition>()
                .Select(i => i.PhaseExitId)
                .ToList();
            Assert.Contains(idBeforeRename, gotoTargets);
        }

        [Test]
        public void RecursiveAndSelf_PhasePlacement_IsRepresentable()
        {
            // Self-call shape per execution-semantics doc:
            //   Start -> Ref(P) -> Ref(P) -> End   (two independent placements of ONE reusable phase)
            // where Ref(P)'s special loop socket re-enters its own placement — pure data.
            var phase = new PhaseDefinition
            {
                Id = "p-self",
                Title = "Self Caller",
                Exits =
                {
                    new PhaseExitDefinition { Id = "px-complete", Name = "Complete" },
                    new PhaseExitDefinition { Id = "px-loop", Name = "Loop" },
                },
            };

            var nodeStart = new SessionStartNodeDefinition { Id = "ns" };
            nodeStart.Outputs.Add(NormalOut("ns-out"));
            var nodeEnd = new SessionEndNodeDefinition { Id = "ne" };
            var placement1 = ReferenceNode("nr-a", phase, out var loopSocketA);
            var placement2 = ReferenceNode("nr-b", phase, out _);

            var sessionGraph = new SessionGraphDefinition
            {
                Nodes = { nodeStart, nodeEnd, placement1, placement2 },
                Edges =
                {
                    EdgeOf("e-start-a", "ns-out", placement1.Id),
                    EdgeOf("e-a-b", CompleteSocket(placement1).Id, placement2.Id),
                    EdgeOf("e-a-loop", loopSocketA.Id, placement2.Id),
                    EdgeOf("e-b-end", CompleteSocket(placement2).Id, nodeEnd.Id),
                },
            };
            var session = new SessionDefinition { Id = "s-self", Title = "Self Call", Graph = sessionGraph };

            var placements = session.Graph.Nodes.OfType<PhaseReferenceNodeDefinition>()
                .Where(n => n.PhaseId == phase.Id)
                .ToList();
            Assert.AreEqual(2, placements.Count, "same reusable Phase placed twice in one session");
            Assert.AreNotSame(placements[0], placements[1], "each placement is its own node");

            // The loop edge proves placement-level wiring (a GOTO exiting phase P on
            // placement A re-enters P through placement B's entrance in this sample).
            CollectionAssert.Contains(
                session.Graph.Edges.Select(e => e.SourceOutputId).ToList(),
                loopSocketA.Id);
        }

        [Test]
        public void RemovedConcepts_AreGone_FromThePortableAssembly()
        {
            var content = Sample();

            Assert.IsNull(typeof(GameContentDefinition).GetProperty("Actions"),
                "reusable configured Actions were replaced by owned instances");
            Assert.IsNull(typeof(CardDefinition).GetProperty("ActionIds"),
                "cards own sequences now");

            var phaseProps = typeof(PhaseDefinition).GetProperties().Select(p => p.Name).ToList();
            Assert.IsFalse(phaseProps.Contains("MinCards"), "min-card progression removed");
            Assert.IsFalse(phaseProps.Contains("MaxCards"), "max-card progression removed");
            Assert.IsFalse(phaseProps.Contains("PhaseSlots"), "session slot lists removed");

            var contentTypeNames = typeof(GameContentDefinition).Assembly.GetTypes()
                .Select(t => t.Name)
                .ToList();
            Assert.IsFalse(contentTypeNames.Any(name => name.Contains("PhaseSlot")),
                "PhaseSlot types were removed from the portable assembly");

            // And every action instance carries only instance-local values.
            foreach (var instance in AllInstances(content))
            {
                Assert.That(instance.Id, Is.Not.Empty);
                Assert.IsTrue(instance.IsBlocking || instance is DebugInstanceDefinition,
                    "sample flow-control-neutral instances are blocking unless authored otherwise");
            }
        }

        // ---- small builders ----

        private static List<string> AllInstanceIds(GameContentDefinition content)
        {
            return AllInstances(content).Select(i => i.Id).ToList();
        }

        private static IEnumerable<ActionInstanceDefinition> AllInstances(GameContentDefinition content)
        {
            foreach (var card in content.Cards)
            {
                if (card?.Sequence != null)
                {
                    foreach (var instance in Enumerate(card.Sequence.Instances)) yield return instance;
                }
            }
            foreach (var phase in content.Phases)
            {
                foreach (var node in phase.Graph.Nodes)
                {
                    if (node is ActionNodeDefinition action && action.Sequence != null)
                    {
                        foreach (var instance in Enumerate(action.Sequence.Instances)) yield return instance;
                    }
                }
            }
        }

        private static IEnumerable<ActionInstanceDefinition> Enumerate(IEnumerable<ActionInstanceDefinition> instances)
        {
            foreach (var instance in instances)
            {
                yield return instance;
                if (instance is PromptChoiceInstanceDefinition choice)
                {
                    foreach (var option in choice.Options)
                    {
                        if (option.Sequence != null)
                        {
                            foreach (var nested in option.Sequence.Instances)
                            {
                                yield return nested;
                            }
                        }
                    }
                }
            }
        }

        private static GraphOutputDefinition NormalOut(string id)
        {
            return new GraphOutputDefinition { Id = id, Kind = GraphPortKind.Normal };
        }

        private static GraphEdgeDefinition EdgeOf(string id, string sourceOutputId, string targetNodeId)
        {
            return new GraphEdgeDefinition { Id = id, SourceOutputId = sourceOutputId, TargetNodeId = targetNodeId };
        }

        private static PhaseReferenceNodeDefinition ReferenceNode(string nodeId, PhaseDefinition phase, out GraphOutputDefinition loopSocket)
        {
            var node = new PhaseReferenceNodeDefinition { Id = nodeId, PhaseId = phase.Id };
            foreach (var exit in phase.Exits)
            {
                var socket = new GraphOutputDefinition
                {
                    Id = $"{nodeId}-socket-{exit.Id}",
                    Kind = GraphPortKind.PhaseExit,
                    PhaseExitId = exit.Id,
                };
                node.Outputs.Add(socket);
            }
            loopSocket = node.Outputs.First(o => o.PhaseExitId == "px-loop");
            return node;
        }

        private static GraphOutputDefinition CompleteSocket(GraphNodeDefinition node)
        {
            return node.Outputs.Single(o => o.PhaseExitId == "px-complete");
        }
    }
}
