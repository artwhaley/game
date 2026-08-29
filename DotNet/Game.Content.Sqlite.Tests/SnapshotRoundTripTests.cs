using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 04 gate: seeding a fresh v2 database from SampleContent and loading
    /// it back must reproduce the same structural content — stable IDs, exits,
    /// graphs, sequences and instance values — byte-for-byte where strings and
    /// floats are concerned.
    /// </summary>
    [TestFixture]
    public class SnapshotRoundTripTests : IDisposable
    {
        private string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-t04-{Guid.NewGuid():N}.db");
            SqliteConnection.ClearAllPools();
        }

        [TearDown]
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void SeedThenLoad_ReproducesStructuralEquivalence()
        {
            var original = SampleContent.Create();

            using (var connection = Open())
            {
                DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(connection, original);
                var loaded = GameContentSnapshotLoader.Load(connection);

                AssertStructuralEquivalence(original, loaded);
            }
        }

        // Canonical-database coverage lives in CanonicalDatabaseTests.

        [Test]
        public void NewActionTypes_RoundTripEveryConfiguredField()
        {
            using (var connection = Open())
            {
                CoreMigrator.EnsureSchema(connection);
                CatalogRepositories.CreateSmartToyCapability(connection,
                    new SmartToyCapabilityDefinition { Id = "vibrate", Title = "Vibration" });
                var card = new CardDefinition { Id = "card-actions", Title = "Actions",
                    Sequence = new ActionSequenceDefinition { Id = "seq-actions" } };
                card.Sequence.Instances.Add(new DialogInstanceDefinition
                    { Id = "dialog", Text = "Hello", IsBlocking = false });
                card.Sequence.Instances.Add(new DelayInstanceDefinition
                    { Id = "delay", DurationSeconds = 2.5f, IsBlocking = true });
                card.Sequence.Instances.Add(new ToyActivityInstanceDefinition
                    { Id = "toy", CapabilityId = "vibrate", Intensity = .7f, DurationSeconds = 4f, IsBlocking = true });
                CardRepository.Create(connection, card);

                var loaded = GameContentSnapshotLoader.LoadSequence(connection, card.Sequence.Id);
                Assert.That(((DialogInstanceDefinition)loaded.Instances[0]).Text, Is.EqualTo("Hello"));
                Assert.That(loaded.Instances[0].IsBlocking, Is.False);
                Assert.That(((DelayInstanceDefinition)loaded.Instances[1]).DurationSeconds, Is.EqualTo(2.5f));
                var toy = (ToyActivityInstanceDefinition)loaded.Instances[2];
                Assert.That(toy.CapabilityId, Is.EqualTo("vibrate"));
                Assert.That(toy.Intensity, Is.EqualTo(.7f));
                Assert.That(toy.DurationSeconds, Is.EqualTo(4f));
                Assert.That(toy.IsBlocking, Is.True);
            }
        }

        [Test]
        public void DuplicateCard_DeepClonesPromptChoiceIdsAndSequences()
        {
            using (var connection = Open())
            {
                CoreMigrator.EnsureSchema(connection);
                var nested = new ActionSequenceDefinition { Id = "source-option-seq" };
                nested.Instances.Add(new DialogInstanceDefinition { Id = "source-dialog", Text = "Original" });
                var prompt = new PromptChoiceInstanceDefinition { Id = "source-prompt", Prompt = "Choose" };
                prompt.Options.Add(new PromptChoiceOptionDefinition
                    { Id = "source-option", Label = "One", Sequence = nested });
                var source = new CardDefinition { Id = "source", Title = "Source",
                    FolderPath = "Romance/Soft",
                    Sequence = new ActionSequenceDefinition { Id = "source-seq" } };
                source.Sequence.Instances.Add(prompt);
                CardRepository.Create(connection, source);

                var clone = CardRepository.Duplicate(connection, "source", "clone", "Clone");
                var sourceReloaded = GameContentSnapshotLoader.LoadSequence(connection, "source-seq");
                var sourceChoice = (PromptChoiceInstanceDefinition)sourceReloaded.Instances.Single();
                var cloneChoice = (PromptChoiceInstanceDefinition)clone.Sequence.Instances.Single();

                Assert.That(clone.FolderPath, Is.EqualTo("Romance/Soft"));
                Assert.That(clone.Sequence.Id, Is.Not.EqualTo(sourceReloaded.Id));
                Assert.That(cloneChoice.Id, Is.Not.EqualTo(sourceChoice.Id));
                Assert.That(cloneChoice.Options.Single().Id, Is.Not.EqualTo(sourceChoice.Options.Single().Id));
                Assert.That(cloneChoice.Options.Single().Sequence.Id,
                    Is.Not.EqualTo(sourceChoice.Options.Single().Sequence.Id));
                Assert.That(cloneChoice.Options.Single().Sequence.Instances.Single().Id,
                    Is.Not.EqualTo(sourceChoice.Options.Single().Sequence.Instances.Single().Id));
            }
        }

        [Test]
        public void CardFolderPath_IsNormalizedStoredAndLoaded()
        {
            using (var connection = Open())
            {
                CoreMigrator.EnsureSchema(connection);
                CardRepository.Create(connection, new CardDefinition
                {
                    Id = "folder-card", Title = "Folder Card", FolderPath = " Romance\\Soft / Favorites ",
                });

                var loaded = GameContentSnapshotLoader.Load(connection).Cards.Single();
                Assert.That(loaded.FolderPath, Is.EqualTo("Romance/Soft/Favorites"));
                CardRepository.SetFolder(connection, loaded.Id, "Intense / Public");
                loaded = GameContentSnapshotLoader.Load(connection).Cards.Single();
                Assert.That(loaded.FolderPath, Is.EqualTo("Intense/Public"));
            }
        }

        [Test]
        public void ChoiceOptionSequences_NestCorrectly()
        {
            using (var connection = Open())
            {
                DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(connection, SampleContent.Create());
                var loaded = GameContentSnapshotLoader.Load(connection);

                var crowdCard = loaded.Cards.Find(c => c.Id == SampleContent.CardFaceTheCrowd);
                Assert.IsNotNull(crowdCard);

                var choice = crowdCard.Sequence.Instances
                    .OfType<PromptChoiceInstanceDefinition>().Single();
                Assert.AreEqual(2, choice.Options.Count);

                var faceIt = choice.Options[0];
                Assert.AreEqual("Face it", faceIt.Label);
                Assert.AreEqual("seq-opt-crowd-face", faceIt.Sequence.Id);
                Assert.AreEqual(2, faceIt.Sequence.Instances.Count,
                    "'Face it' runs stat increase then progress");

                var shrugItOff = choice.Options[1];
                Assert.AreEqual(1, shrugItOff.Sequence.Instances.Count);
                Assert.IsFalse(shrugItOff.Sequence.Instances[0].IsBlocking,
                    "the Shrug-it-off debug line is authored nonblocking");
            }
        }

        [Test]
        public void Load_FailsLoudly_OnFlowControlInstanceMarkedNonblocking()
        {
            using (var connection = Open())
            {
                DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(connection, SampleContent.Create());
                var sessionEndInstanceId = Scalar(connection,
                    "SELECT ai.id FROM action_instance ai WHERE ai.action_type='end_session' LIMIT 1") as string;
                Assume.That(sessionEndInstanceId, Is.Not.Null);

                using (var transaction = connection.BeginTransaction())
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "UPDATE action_instance SET is_blocking=0 WHERE id=@id;";
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = "@id";
                        parameter.Value = sessionEndInstanceId;
                        command.Parameters.Add(parameter);
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }

                Assert.Throws<System.InvalidOperationException>(() => GameContentSnapshotLoader.Load(connection));
            }
        }

        [Test]
        public void Load_FailsLoudly_OnNestedSessionGotoWithoutDirectDecisionOwnership()
        {
            var content = new GameContentDefinition();
            content.SessionTypes.Add(new SessionTypeDefinition { Id = "type", Title = "Type" });

            var start = new SessionStartNodeDefinition { Id = "start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "start-out", Kind = GraphPortKind.Normal });
            var decision = new SessionDecisionNodeDefinition { Id = "decision", Prompt = "Choose" };
            decision.Outputs.Add(new GraphOutputDefinition { Id = "decision-out", Kind = GraphPortKind.Normal });
            decision.Options.Add(new SessionDecisionOptionDefinition
            {
                Id = "decision-option",
                Label = "Nested",
                Sequence = new ActionSequenceDefinition
                {
                    Id = "decision-sequence",
                    Instances =
                    {
                        new PromptChoiceInstanceDefinition
                        {
                            Id = "prompt",
                            Prompt = "Nested choice",
                            Options =
                            {
                                new PromptChoiceOptionDefinition
                                {
                                    Id = "prompt-option",
                                    Label = "Illegal goto",
                                    Sequence = new ActionSequenceDefinition
                                    {
                                        Id = "prompt-sequence",
                                        Instances =
                                        {
                                            new SessionGotoInstanceDefinition
                                            {
                                                Id = "nested-goto",
                                                Label = "Must be rejected",
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            });
            var end = new SessionEndNodeDefinition { Id = "end" };
            content.Sessions.Add(new SessionDefinition
            {
                Id = "session",
                Title = "Session",
                SessionTypeId = "type",
                Graph = new SessionGraphDefinition
                {
                    Nodes = { start, decision, end },
                    Edges =
                    {
                        new GraphEdgeDefinition { Id = "edge-start", SourceOutputId = "start-out", TargetNodeId = "decision" },
                        new GraphEdgeDefinition { Id = "edge-end", SourceOutputId = "decision-out", TargetNodeId = "end" },
                    },
                },
            });

            using (var connection = Open())
            {
                DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(connection, content);
                var exception = Assert.Throws<System.InvalidOperationException>(
                    () => GameContentSnapshotLoader.Load(connection));
                StringAssert.Contains("SessionDecision option", exception.Message);
                StringAssert.Contains("nested-goto", exception.ToString());
            }
        }

        [Test]
        public void SharedSequenceReferencedTwice_LoadsAsOneInstance()
        {
            using (var connection = Open())
            {
                // Author one phase whose action node references an existing card sequence id.
                var fixture = SampleContent.Create();
                var sharedSequenceId = fixture.Cards[0].Sequence.Id;
                var extraPhase = fixture.Phases[0];

                foreach (var node in extraPhase.Graph.Nodes)
                {
                    if (node is ActionNodeDefinition actionNode)
                    {
                        // Re-point the first action node's sequence at the card's owned sequence row.
                        actionNode.Sequence = new ActionSequenceDefinition { Id = sharedSequenceId };
                        break;
                    }
                }

                DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(connection, fixture);

                var loaded = GameContentSnapshotLoader.Load(connection);
                var loadedPhase = loaded.Phases.Find(p => p.Id == extraPhase.Id);
                Assert.IsNotNull(loadedPhase, "mutated phase survived the round trip");

                ActionNodeDefinition sharedReference = null;
                foreach (var node in loadedPhase.Graph.Nodes)
                {
                    if (node is ActionNodeDefinition candidate && candidate.Sequence.Id == sharedSequenceId)
                    {
                        sharedReference = candidate;
                        break;
                    }
                }
                Assert.IsNotNull(sharedReference, "phase action node shares the card's sequence row");
                var loadedCard = loaded.Cards.Find(c => c.Id == fixture.Cards[0].Id);
                Assert.IsNotNull(loadedCard);
                Assert.AreSame(loadedCard.Sequence, sharedReference.Sequence,
                    "loader must yield ONE object for one stored sequence row");
            }
        }

        // ---- helpers ----

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection("Data Source=" + _dbPath);
            connection.Open();
            return connection;
        }

        private static object Scalar(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
        }

        private static void AssertStructuralEquivalence(GameContentDefinition expected, GameContentDefinition actual)
        {
            Assert.AreEqual(expected.SessionTypes.Count, actual.SessionTypes.Count, "session types");
            Assert.AreEqual(expected.Temperatures.Count, actual.Temperatures.Count, "temperatures");
            Assert.AreEqual(expected.Resources.Count, actual.Resources.Count, "resources");
            Assert.AreEqual(expected.CardTagDefinitions.Count, actual.CardTagDefinitions.Count, "card tags");
            Assert.AreEqual(expected.Cards.Count, actual.Cards.Count, "cards");

            foreach (var expectedCard in expected.Cards)
            {
                var actualCard = actual.Cards.Find(c => c.Id == expectedCard.Id);
                Assert.IsNotNull(actualCard);
                Assert.AreEqual(expectedCard.Title, actualCard.Title);
                Assert.AreEqual(expectedCard.BodyText, actualCard.BodyText);
                Assert.AreEqual(expectedCard.FolderPath, actualCard.FolderPath);
                CollectionAssert.AreEqual(expectedCard.CardTagIds, actualCard.CardTagIds);
                CollectionAssert.AreEqual(expectedCard.KinkIds, actualCard.KinkIds);
                CollectionAssert.AreEqual(expectedCard.RequiredEquipmentIds, actualCard.RequiredEquipmentIds);
                CollectionAssert.AreEqual(expectedCard.RequiredCapabilityIds, actualCard.RequiredCapabilityIds);
                AssertSequencesEqual(expectedCard.Sequence, actualCard.Sequence);
            }

            foreach (var expectedPhase in expected.Phases)
            {
                var actualPhase = actual.Phases.Find(p => p.Id == expectedPhase.Id);
                Assert.IsNotNull(actualPhase);
                Assert.AreEqual(expectedPhase.Title, actualPhase.Title);
                CollectionAssert.AreEqual(expectedPhase.MustHaveAllCardTags, actualPhase.MustHaveAllCardTags);
                CollectionAssert.AreEqual(expectedPhase.MustHaveAnyCardTags, actualPhase.MustHaveAnyCardTags);
                Assert.AreEqual(expectedPhase.Exits.Count, actualPhase.Exits.Count, $"exits on {expectedPhase.Id}");

                for (var i = 0; i < expectedPhase.Exits.Count; i++)
                {
                    Assert.AreEqual(expectedPhase.Exits[i].Id, actualPhase.Exits[i].Id);
                    Assert.AreEqual(expectedPhase.Exits[i].Name, actualPhase.Exits[i].Name);
                }

                Assert.AreEqual(expectedPhase.Graph.Nodes.Count, actualPhase.Graph.Nodes.Count);
                foreach (var node in expectedPhase.Graph.Nodes)
                {
                    var actualNode = FindNode(actualPhase.Graph.Nodes, node.Id);
                    Assert.IsNotNull(actualNode, $"node {node.Id} in {expectedPhase.Id}");
                    AssertOutputsMatch(node, actualNode);
                }
                Assert.AreEqual(expectedPhase.Graph.Edges.Count, actualPhase.Graph.Edges.Count,
                    $"edges on {expectedPhase.Id}");
                foreach (var edge in expectedPhase.Graph.Edges)
                {
                    Assert.IsNotNull(FindEdge(actualPhase.Graph.Edges, edge.Id));
                }
            }

            foreach (var expectedSession in expected.Sessions)
            {
                var actualSession = actual.Sessions.Find(s => s.Id == expectedSession.Id);
                Assert.IsNotNull(actualSession);
                Assert.AreEqual(expectedSession.Title, actualSession.Title);
                Assert.AreEqual(expectedSession.SessionTypeId, actualSession.SessionTypeId);
                Assert.AreEqual(expectedSession.Graph.Nodes.Count, actualSession.Graph.Nodes.Count);
                Assert.AreEqual(expectedSession.Graph.Edges.Count, actualSession.Graph.Edges.Count);
                foreach (var node in expectedSession.Graph.Nodes)
                {
                    var actualNode = FindNode(actualSession.Graph.Nodes, node.Id);
                    Assert.IsNotNull(actualNode, $"node {node.Id} in {expectedSession.Id}");
                    AssertOutputsMatch(node, actualNode);

                    if (node is PhaseReferenceNodeDefinition reference)
                    {
                        Assert.AreEqual(reference.PhaseId, ((PhaseReferenceNodeDefinition)actualNode).PhaseId);
                    }
                }
            }
        }

        private static GraphNodeDefinition FindNode(System.Collections.Generic.IEnumerable<GraphNodeDefinition> nodes, string id)
        {
            foreach (var node in nodes)
            {
                if (node.Id == id) return node;
            }
            return null;
        }

        private static GraphEdgeDefinition FindEdge(System.Collections.Generic.List<GraphEdgeDefinition> edges, string id)
        {
            foreach (var edge in edges)
            {
                if (edge.Id == id) return edge;
            }
            return null;
        }

        private static void AssertOutputsMatch(GraphNodeDefinition expected, GraphNodeDefinition actual)
        {
            Assert.AreEqual(expected.Outputs.Count, actual.Outputs.Count, $"outputs on '{expected.Id}'");
            for (var i = 0; i < expected.Outputs.Count; i++)
            {
                var e = expected.Outputs[i];
                var a = actual.Outputs[i];
                Assert.AreEqual(e.Id, a.Id, $"output order on '{expected.Id}'");
                Assert.AreEqual(e.Kind, a.Kind);
                Assert.AreEqual(e.PhaseExitId, a.PhaseExitId);
                Assert.AreEqual(e.SessionGotoActionInstanceId, a.SessionGotoActionInstanceId);
                Assert.AreEqual(e.Label, a.Label);
            }
        }

        private static void AssertSequencesEqual(ActionSequenceDefinition expected, ActionSequenceDefinition actual)
        {
            Assert.IsNotNull(actual);
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Instances.Count, actual.Instances.Count);

            for (var i = 0; i < expected.Instances.Count; i++)
            {
                var e = expected.Instances[i];
                var a = actual.Instances[i];
                Assert.AreEqual(e.GetType(), a.GetType(), $"instance type at {i} in '{expected.Id}'");
                Assert.AreEqual(e.Id, a.Id);
                Assert.AreEqual(e.IsBlocking, a.IsBlocking);

                switch (e)
                {
                    case DebugInstanceDefinition ed:
                        var ad = (DebugInstanceDefinition)a;
                        Assert.AreEqual(ed.Message, ad.Message);
                        Assert.AreEqual(ed.DelaySeconds, ad.DelaySeconds);
                        break;
                    case StatIncreaseInstanceDefinition es:
                        var asn = (StatIncreaseInstanceDefinition)a;
                        Assert.AreEqual(es.StatKey, asn.StatKey);
                        Assert.AreEqual(es.Amount, asn.Amount);
                        break;
                    case IncrementProgressInstanceDefinition ep:
                        Assert.AreEqual(ep.Amount, ((IncrementProgressInstanceDefinition)a).Amount);
                        break;
                    case ModifyTemperatureInstanceDefinition em:
                        var amt = (ModifyTemperatureInstanceDefinition)a;
                        Assert.AreEqual(em.TemperatureId, amt.TemperatureId);
                        Assert.AreEqual(em.Amount, amt.Amount);
                        break;
                    case CutsceneInstanceDefinition ec:
                        Assert.AreEqual(ec.ResourceId, ((CutsceneInstanceDefinition)a).ResourceId);
                        break;
                    case PromptChoiceInstanceDefinition echoice:
                        var achoice = (PromptChoiceInstanceDefinition)a;
                        Assert.AreEqual(echoice.Prompt, achoice.Prompt);
                        Assert.AreEqual(echoice.Options.Count, achoice.Options.Count);
                        for (var o = 0; o < echoice.Options.Count; o++)
                        {
                            Assert.AreEqual(echoice.Options[o].Id, achoice.Options[o].Id);
                            Assert.AreEqual(echoice.Options[o].Label, achoice.Options[o].Label);
                            AssertSequencesEqual(echoice.Options[o].Sequence, achoice.Options[o].Sequence);
                        }
                        break;
                    case PhaseGotoInstanceDefinition eg:
                        Assert.AreEqual(eg.PhaseExitId, ((PhaseGotoInstanceDefinition)a).PhaseExitId);
                        break;
                    case SessionGotoInstanceDefinition esg:
                        Assert.AreEqual(esg.Label, ((SessionGotoInstanceDefinition)a).Label);
                        break;
                }
            }
        }
    }
}
