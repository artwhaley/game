using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 14: phase library CRUD + reusable phase graph authoring
    /// persistence. Authored phase nodes/edges round-trip through the v2 loader;
    /// usage counts drive the Phase header; tag replacement is tested end-to-end.
    /// </summary>
    [TestFixture]
    public class PhaseAuthoringTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-phauth-{Guid.NewGuid():N}.db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            _connection.Open();
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

        // ---------- phase metadata ----------

        [Test]
        public void CreateWithEntry_CreatesSingularEntryAndInitialLayout()
        {
            PhaseRepository.CreateWithEntry(_connection, "p1", "Cold Start");

            var graph = GameContentSnapshotLoader.Load(_connection).Phases.Find(p => p.Id == "p1").Graph;
            Assert.AreEqual(1, graph.Nodes.Count);
            Assert.IsInstanceOf<PhaseEntryNodeDefinition>(graph.Nodes[0]);
            var layout = AuthoringLayoutRepository.LoadPhaseNodePositions(_connection, "p1");
            Assert.AreEqual(0, layout[graph.Nodes[0].Id].X, 0.001);
            Assert.AreEqual(0, layout[graph.Nodes[0].Id].Y, 0.001);
            Assert.Throws<InvalidOperationException>(() => PhaseGraphRepository.AddNode(
                _connection, "p1", new PhaseEntryNodeDefinition { Id = "another-entry" }));
        }

        [Test]
        public void CreateRenameCardQuery_AndLoaderRoundTrip()
        {
            PhaseRepository.Create(_connection, "p1", "Cold Start");
            CatalogRepositories.CreateCardTag(_connection, new CardTagDefinition { Id = "tag-cold", Title = "Cold" });
            CatalogRepositories.CreateCardTag(_connection, new CardTagDefinition { Id = "tag-opening", Title = "Opening" });
            CatalogRepositories.CreateCardTag(_connection, new CardTagDefinition { Id = "tag-boss", Title = "Boss" });
            PhaseRepository.ReplaceCardQuery(_connection, "p1",
                new[] { "tag-cold", "tag-opening" }, new[] { "tag-boss" });

            var content = GameContentSnapshotLoader.Load(_connection);
            var phase = content.Phases.Find(p => p.Id == "p1");
            Assert.IsNotNull(phase);
            Assert.AreEqual("Cold Start", phase.Title);
            CollectionAssert.AreEqual(new[] { "tag-cold", "tag-opening" }, phase.MustHaveAllCardTags);
            CollectionAssert.AreEqual(new[] { "tag-boss" }, phase.MustHaveAnyCardTags);
        }

        [Test]
        public void Rename_UpdatesTitle()
        {
            PhaseRepository.Create(_connection, "p1", "Old");
            PhaseRepository.Rename(_connection, "p1", "New");

            var content = GameContentSnapshotLoader.Load(_connection);
            Assert.AreEqual("New", content.Phases.Find(p => p.Id == "p1").Title);
        }

        [Test]
        public void DeleteReferencedPhase_IsBlocked_Unreferenced_Cascades()
        {
            PhaseRepository.Create(_connection, "p-free", "Free");
            PhaseRepository.Create(_connection, "p-used", "Used");
            Execute("INSERT INTO phase_graph_node VALUES ('n1','p-free','Entry');");

            // Referenced phase is blocked by the session placement RESTRICT FK.
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            Execute("INSERT INTO session_graph_node VALUES ('ref','s1','PhaseReference');");
            Execute("INSERT INTO session_node_phase VALUES ('ref','p-used');");

            Assert.Throws<SqliteException>(() => PhaseRepository.Delete(_connection, "p-used"));

            // Unreferenced phase deletes and cascades its graph.
            PhaseRepository.Delete(_connection, "p-free");
            Assert.AreEqual(0, Count("phase_graph_node WHERE phase_id='p-free'"));
            Assert.AreEqual(0, Count("phase WHERE id='p-free'"));
        }

        [Test]
        public void DeletePhase_WithOwnedPhaseGoto_DetachesRestrictReferenceAndCascades()
        {
            PhaseRepository.Create(_connection, "p1", "Phase with goto");
            PhaseExitRepository.Create(_connection, "p1",
                new PhaseExitDefinition { Id = "px-complete", Name = "Complete" }, 0);
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            PhaseGraphRepository.AddNode(_connection, "p1", action);
            PhaseGraphRepository.AddPhaseGoto(_connection, "n-action", "px-complete");

            Assert.DoesNotThrow(() => PhaseRepository.Delete(_connection, "p1"));
            Assert.AreEqual(0, Count("phase WHERE id='p1'"));
            Assert.AreEqual(0, Count("action_instance_phase_goto"));
            Assert.AreEqual(0, Count("action_instance"));
            Assert.AreEqual(0, Count("action_sequence"));
        }

        [Test]
        public void Usage_CountsDistinctSessionsAndPlacements()
        {
            PhaseRepository.Create(_connection, "p1", "Cold Start");
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            SessionRepository.Create(_connection, "s2", "Beta", "type-standard");
            Execute("INSERT INTO session_graph_node VALUES ('r1','s1','PhaseReference');");
            Execute("INSERT INTO session_graph_node VALUES ('r2','s1','PhaseReference');");
            Execute("INSERT INTO session_graph_node VALUES ('r3','s2','PhaseReference');");
            Execute("INSERT INTO session_node_phase VALUES ('r1','p1');");
            Execute("INSERT INTO session_node_phase VALUES ('r2','p1');");
            Execute("INSERT INTO session_node_phase VALUES ('r3','p1');");

            var (sessions, placements) = PhaseRepository.Usage(_connection, "p1");
            Assert.AreEqual(2, sessions);
            Assert.AreEqual(3, placements);
        }

        // ---------- phase node authoring ----------

        [Test]
        public void AddEntry_Check_Action_Return_PersistsAndRoundTrips()
        {
            PhaseRepository.Create(_connection, "p1", "Cold Start");

            var entry = new PhaseEntryNodeDefinition { Id = "n-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = "n-entry-out", Kind = GraphPortKind.Normal });
            PhaseGraphRepository.AddNode(_connection, "p1", entry);

            var check = new VariableCheckNodeDefinition
            {
                Id = "n-check",
                SourceKind = VariableSourceKind.Temperature,
                VariableKey = "happiness",
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 5f,
            };
            check.Outputs.Add(new GraphOutputDefinition { Id = "n-check-t", Kind = GraphPortKind.True });
            check.Outputs.Add(new GraphOutputDefinition { Id = "n-check-f", Kind = GraphPortKind.False });
            PhaseGraphRepository.AddNode(_connection, "p1", check);

            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            action.Outputs.Add(new GraphOutputDefinition { Id = "n-action-out", Kind = GraphPortKind.Normal });
            PhaseGraphRepository.AddNode(_connection, "p1", action);

            PhaseGraphRepository.AddNode(_connection, "p1", new ReturnNodeDefinition { Id = "n-return" });

            var content = GameContentSnapshotLoader.Load(_connection);
            var graph = content.Phases.Find(p => p.Id == "p1").Graph;
            Assert.AreEqual(4, graph.Nodes.Count);
            Assert.IsInstanceOf<PhaseEntryNodeDefinition>(graph.Nodes.Find(n => n.Id == "n-entry"));
            Assert.IsInstanceOf<ReturnNodeDefinition>(graph.Nodes.Find(n => n.Id == "n-return"));

            var loadedCheck = (VariableCheckNodeDefinition)graph.Nodes.Find(n => n.Id == "n-check");
            Assert.AreEqual(VariableSourceKind.Temperature, loadedCheck.SourceKind);
            Assert.AreEqual("happiness", loadedCheck.VariableKey);
            Assert.AreEqual(VariableCompareOperator.GreaterThanOrEqual, loadedCheck.Operator);
            Assert.AreEqual(5f, loadedCheck.CompareValue, 0.001);

            var loadedAction = (ActionNodeDefinition)graph.Nodes.Find(n => n.Id == "n-action");
            Assert.IsNotNull(loadedAction.Sequence);
        }

        [Test]
        public void UpdateVariableCheck_PersistsNewComparison()
        {
            PhaseRepository.Create(_connection, "p1", "Cold Start");
            var check = new VariableCheckNodeDefinition
            {
                Id = "n-check",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.LessThan,
                CompareValue = 3f,
            };
            PhaseGraphRepository.AddNode(_connection, "p1", check);

            PhaseGraphRepository.UpdateVariableCheck(_connection, "n-check",
                VariableSourceKind.Stat, "courage", VariableCompareOperator.GreaterThan, 7f);

            var content = GameContentSnapshotLoader.Load(_connection);
            var loaded = (VariableCheckNodeDefinition)content.Phases.Find(p => p.Id == "p1")
                .Graph.Nodes.Find(n => n.Id == "n-check");
            Assert.AreEqual(VariableSourceKind.Stat, loaded.SourceKind);
            Assert.AreEqual("courage", loaded.VariableKey);
            Assert.AreEqual(VariableCompareOperator.GreaterThan, loaded.Operator);
            Assert.AreEqual(7f, loaded.CompareValue, 0.001);
        }

        [Test]
        public void PhaseNodeLayout_SavesLoads()
        {
            PhaseRepository.Create(_connection, "p1", "Cold Start");
            PhaseGraphRepository.AddNode(_connection, "p1", new PhaseEntryNodeDefinition { Id = "n-entry" });

            AuthoringLayoutRepository.SavePhaseNodePosition(_connection, "p1", "n-entry", 15, 25);
            var layout = AuthoringLayoutRepository.LoadPhaseNodePositions(_connection, "p1");
            Assert.AreEqual(15, layout["n-entry"].X, 0.001);
            Assert.AreEqual(25, layout["n-entry"].Y, 0.001);
        }

        [Test]
        public void RemoveEntry_IsRejected_ByStructuralInvariant()
        {
            PhaseRepository.CreateWithEntry(_connection, "p1", "Cold Start");
            var entry = GameContentSnapshotLoader.Load(_connection).Phases.Find(p => p.Id == "p1").Graph.Nodes[0];
            Assert.Throws<InvalidOperationException>(() => PhaseGraphRepository.RemoveNode(_connection, "p1", entry.Id));
            Assert.AreEqual(1, Count("phase_graph_node"));
        }

        // ---------- helpers ----------

        private long Count(string fromClause)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) " +
                    (fromClause.TrimStart().StartsWith("FROM", StringComparison.OrdinalIgnoreCase)
                        ? fromClause : "FROM " + fromClause);
                return Convert.ToInt64(command.ExecuteScalar());
            }
        }

        private void Execute(string sql)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
