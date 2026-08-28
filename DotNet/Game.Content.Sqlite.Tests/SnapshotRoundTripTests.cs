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
            CollectionAssert.AreEqual(expected.Deck.CardIds, actual.Deck.CardIds, "deck order");

            CollectionAssert.AreEquivalent(
                KeysOf(expected.Cards), KeysOf(actual.Cards), "card ids preserved");

            foreach (var expectedCard in expected.Cards)
            {
                var actualCard = actual.Cards.Find(c => c.Id == expectedCard.Id);
                Assert.IsNotNull(actualCard);
                Assert.AreEqual(expectedCard.Title, actualCard.Title);
                CollectionAssert.AreEqual(expectedCard.Tags, actualCard.Tags);
                AssertSequencesEqual(expectedCard.Sequence, actualCard.Sequence);
            }

            foreach (var expectedPhase in expected.Phases)
            {
                var actualPhase = actual.Phases.Find(p => p.Id == expectedPhase.Id);
                Assert.IsNotNull(actualPhase);
                Assert.AreEqual(expectedPhase.Title, actualPhase.Title);
                CollectionAssert.AreEqual(expectedPhase.MustIncludeTags, actualPhase.MustIncludeTags);
                CollectionAssert.AreEqual(expectedPhase.MustExcludeTags, actualPhase.MustExcludeTags);
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

        private static System.Collections.Generic.IEnumerable<string> KeysOf(
            System.Collections.Generic.List<CardDefinition> cards)
        {
            foreach (var card in cards) yield return card.Id;
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
