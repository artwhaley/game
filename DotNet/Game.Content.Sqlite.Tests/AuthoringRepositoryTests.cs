using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 04: the v2 narrow authoring repositories. Each test drives the
    /// repository through its normal operation and reloads via the snapshot
    /// loader (or direct SQL) to prove what actually persisted.
    /// </summary>
    [TestFixture]
    public class AuthoringRepositoryTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "gwb-t04-repos-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);
        }

        [TearDown]
        public void Dispose()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private static string Id() => "id-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        // ---------- reference entities ----------

        [Test]
        public void SessionType_CreateRenameList()
        {
            SessionTypeRepository.Create(_connection, "type-test", "Test");
            SessionTypeRepository.Rename(_connection, "type-test", "Renamed");

            var types = SessionTypeRepository.List(_connection);
            var testType = types.Find(t => t.Id == "type-test");
            Assert.IsNotNull(testType);
            Assert.AreEqual("Renamed", testType.Title);
        }

        [Test]
        public void Temperature_CreateUpdateList()
        {
            TemperatureRepository.Create(_connection, new TemperatureDefinition
            {
                Id = "temp-test",
                Title = "Tension",
                MinValue = 0f,
                MaxValue = 10f,
                DefaultValue = 5f,
            });
            TemperatureRepository.Update(_connection, new TemperatureDefinition
            {
                Id = "temp-test",
                Title = "Tension II",
                MinValue = 0f,
                MaxValue = 20f,
                DefaultValue = 7f,
            });

            var temperatures = TemperatureRepository.List(_connection);
            var updated = temperatures.Find(t => t.Id == "temp-test");
            Assert.IsNotNull(updated);
            Assert.AreEqual("Tension II", updated.Title);
            Assert.AreEqual(20f, updated.MaxValue);
            Assert.AreEqual(7f, updated.DefaultValue);
        }

        // ---------- phase graph ----------

        [Test]
        public void PhaseGraph_ReplaceGraph_CascadesAndRewrites()
        {
            var phaseId = Id();
            Sql.Execute(_connection, null,
                "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, 'P', 0, 0);",
                ("id", phaseId));

            var firstGraph = new PhaseGraphDefinition();
            var entry = new PhaseEntryNodeDefinition { Id = $"pn-{phaseId}-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = $"pn-{phaseId}-entry-out", Kind = GraphPortKind.Normal });
            firstGraph.Nodes.Add(entry);

            PhaseGraphRepository.ReplaceGraph(_connection, phaseId, firstGraph);
            Assert.AreEqual(1L, Scalar("SELECT COUNT(*) FROM phase_graph_node WHERE phase_id=@p;", phaseId));
            Assert.AreEqual(1L, Scalar("SELECT COUNT(*) FROM phase_node_output WHERE node_id=@p;", $"pn-{phaseId}-entry"));

            // Replace with another valid graph: the old node/socket is removed
            // and the singular Entry replacement is written.
            PhaseGraphRepository.ReplaceGraph(_connection, phaseId, new PhaseGraphDefinition
            {
                Nodes = { new PhaseEntryNodeDefinition { Id = "entry-replacement" } },
            });
            Assert.AreEqual(1L, Scalar("SELECT COUNT(*) FROM phase_graph_node WHERE phase_id=@p;", phaseId));
            Assert.AreEqual(0L, Scalar("SELECT COUNT(*) FROM phase_node_output WHERE node_id=@p;", $"pn-{phaseId}-entry"));
        }

        [Test]
        public void PhaseGraph_AddRemoveEdge_AndRemoveNode()
        {
            var phaseId = Id();
            Sql.Execute(_connection, null,
                "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, 'P', 0, 0);",
                ("id", phaseId));

            var entry = new PhaseEntryNodeDefinition { Id = $"pn-{phaseId}-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = $"pn-{phaseId}-entry-out", Kind = GraphPortKind.Normal });
            var executor = new CardExecutorNodeDefinition { Id = $"pn-{phaseId}-exec" };
            var graph = new PhaseGraphDefinition();
            graph.Nodes.Add(entry);
            graph.Nodes.Add(executor);
            PhaseGraphRepository.ReplaceGraph(_connection, phaseId, graph);

            var edge = new GraphEdgeDefinition
            {
                Id = $"pe-{phaseId}-x",
                SourceOutputId = $"pn-{phaseId}-entry-out",
                TargetNodeId = $"pn-{phaseId}-exec",
            };
            PhaseGraphRepository.AddEdge(_connection, phaseId, edge);
            Assert.AreEqual(1L, Scalar("SELECT COUNT(*) FROM phase_graph_edge WHERE id=@p;", edge.Id));

            PhaseGraphRepository.RemoveEdge(_connection, edge.Id);
            Assert.AreEqual(0L, Scalar("SELECT COUNT(*) FROM phase_graph_edge WHERE id=@p;", edge.Id));

            PhaseGraphRepository.RemoveNode(_connection, phaseId, executor.Id);
            Assert.AreEqual(0L, Scalar("SELECT COUNT(*) FROM phase_graph_node WHERE id=@p;", executor.Id));
        }

        // ---------- session graph ----------

        [Test]
        public void SessionGraph_ReplaceGraph_RoundTripsThroughLoader()
        {
            var sessionId = Id();
            SessionTypeRepository.Create(_connection, "type-s", "Standard");
            Sql.Execute(_connection, null,
                "INSERT INTO session (id, title, session_type_id) VALUES (@id, 'S', 'type-s');",
                ("id", sessionId));

            var graph = new SessionGraphDefinition();
            var start = new SessionStartNodeDefinition { Id = $"sn-{sessionId}-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = $"sn-{sessionId}-start-out", Kind = GraphPortKind.Normal });
            var end = new SessionEndNodeDefinition { Id = $"sn-{sessionId}-end" };
            graph.Nodes.Add(start);
            graph.Nodes.Add(end);
            graph.Edges.Add(new GraphEdgeDefinition
            {
                Id = $"se-{sessionId}-1",
                SourceOutputId = $"sn-{sessionId}-start-out",
                TargetNodeId = end.Id,
            });

            SessionGraphRepository.ReplaceGraph(_connection, sessionId, graph);

            var loaded = GameContentSnapshotLoader.Load(_connection);
            var session = loaded.Sessions.Find(s => s.Id == sessionId);
            Assert.IsNotNull(session);
            Assert.AreEqual(2, session.Graph.Nodes.Count);
            Assert.AreEqual(1, session.Graph.Edges.Count);
        }

        // ---------- action sequences / cards ----------

        [Test]
        public void ActionSequence_SaveExistsDelete()
        {
            var sequence = new ActionSequenceDefinition
            {
                Id = "seq-repo-1",
                Instances =
                {
                    new StatIncreaseInstanceDefinition { Id = "inst-repo-1", StatKey = "courage", Amount = 2f },
                    new DebugInstanceDefinition { Id = "inst-repo-2", Message = "hello" },
                },
            };

            ActionSequenceRepository.Save(_connection, sequence);
            Assert.IsTrue(ActionSequenceRepository.Exists(_connection, "seq-repo-1"));
            Assert.AreEqual(2L, Scalar("SELECT COUNT(*) FROM action_instance WHERE action_sequence_id=@p;", "seq-repo-1"));

            ActionSequenceRepository.Delete(_connection, "seq-repo-1");
            Assert.IsFalse(ActionSequenceRepository.Exists(_connection, "seq-repo-1"));
            Assert.AreEqual(0L, Scalar("SELECT COUNT(*) FROM action_instance WHERE action_sequence_id=@p;", "seq-repo-1"));
        }

        [Test]
        public void Card_CreateWithOwnedSequence_LoadsWithDefaultProgress()
        {
            var cardId = Id();
            CatalogRepositories.CreateCardTag(_connection, new CardTagDefinition { Id = "tag-truth", Title = "Truth" });
            var card = new CardDefinition
            {
                Id = cardId,
                Title = "Repo Card",
                CardTagIds = { "tag-truth" },
                Sequence = new ActionSequenceDefinition
                {
                    Id = $"cseq-{cardId}",
                    Instances =
                    {
                        new IncrementProgressInstanceDefinition { Id = $"inst-{cardId}-progress", Amount = 10f },
                    },
                },
            };

            CardRepository.Create(_connection, card);

            var loaded = GameContentSnapshotLoader.Load(_connection);
            var reloaded = loaded.Cards.Find(c => c.Id == cardId);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual("Repo Card", reloaded.Title);
            CollectionAssert.AreEqual(new[] { "tag-truth" }, reloaded.CardTagIds);
            Assert.AreEqual(1, reloaded.Sequence.Instances.Count);
            Assert.AreEqual(10f, ((IncrementProgressInstanceDefinition)reloaded.Sequence.Instances[0]).Amount);
        }

        [Test]
        public void Card_CreateWithoutSequence_GetsDefaultWaitThenProgressActions()
        {
            var cardId = Id();
            var card = new CardDefinition { Id = cardId, Title = "Defaulted" };

            CardRepository.Create(_connection, card);

            var loaded = GameContentSnapshotLoader.Load(_connection);
            var reloaded = loaded.Cards.Find(c => c.Id == cardId);
            Assert.IsNotNull(reloaded);
            Assert.IsNotNull(reloaded.Sequence, "repository creates the owned sequence");
            Assert.AreEqual(2, reloaded.Sequence.Instances.Count);
            Assert.IsInstanceOf<WaitForContinueInstanceDefinition>(reloaded.Sequence.Instances[0]);
            var progress = reloaded.Sequence.Instances[1] as IncrementProgressInstanceDefinition;
            Assert.IsNotNull(progress, "default instance is IncrementProgress");
            Assert.AreEqual(10f, progress.Amount, "default amount is 10");
        }

        [Test]
        public void Card_DefaultProgress_IsPerCard_NotShared()
        {
            var first = new CardDefinition
            {
                Id = Id(),
                Title = "First",
                Sequence = new ActionSequenceDefinition
                {
                    Id = "seq-first",
                    Instances =
                    {
                        new IncrementProgressInstanceDefinition { Id = "inst-first", Amount = 25f },
                    },
                },
            };
            var second = new CardDefinition { Id = Id(), Title = "Second" }; // gets default wait + 10

            CardRepository.Create(_connection, first);
            CardRepository.Create(_connection, second);

            var loaded = GameContentSnapshotLoader.Load(_connection);
            var reloadedFirst = loaded.Cards.Find(c => c.Id == first.Id);
            var reloadedSecond = loaded.Cards.Find(c => c.Id == second.Id);

            Assert.AreEqual(25f, ((IncrementProgressInstanceDefinition)reloadedFirst.Sequence.Instances[0]).Amount,
                "authored amount survives untouched");
            Assert.AreEqual(10f, ((IncrementProgressInstanceDefinition)reloadedSecond.Sequence.Instances[1]).Amount,
                "defaulted card keeps its own default; changing one card never affects another");
        }

        // ---------- phase exits ----------

        [Test]
        public void PhaseExit_CreateListDelete()
        {
            var phaseId = Id();
            Sql.Execute(_connection, null,
                "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, 'P', 0, 0);",
                ("id", phaseId));

            PhaseExitRepository.Create(_connection, phaseId, new PhaseExitDefinition { Id = $"px-{phaseId}-done", Name = "Complete" }, 0);
            PhaseExitRepository.Create(_connection, phaseId, new PhaseExitDefinition { Id = $"px-{phaseId}-fail", Name = "Fail" }, 1);

            var exits = PhaseExitRepository.List(_connection, phaseId);
            Assert.AreEqual(2, exits.Count);
            Assert.AreEqual("Complete", exits[0].Name);
            Assert.AreEqual("Fail", exits[1].Name);

            PhaseExitRepository.Delete(_connection, $"px-{phaseId}-fail");
            exits = PhaseExitRepository.List(_connection, phaseId);
            Assert.AreEqual(1, exits.Count);
        }

        // ---------- helpers ----------

        private object Scalar(string sql, string parameterValue)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@p";
                parameter.Value = parameterValue;
                command.Parameters.Add(parameter);
                return command.ExecuteScalar();
            }
        }

    }
}
