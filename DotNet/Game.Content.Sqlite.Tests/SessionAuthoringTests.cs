using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 13: session library CRUD + session graph authoring persistence.
    /// Every authored node/edge/layout row round-trips through the v2 loader,
    /// so the graph the workbench displays always mirrors the database.
    /// </summary>
    [TestFixture]
    public class SessionAuthoringTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-sauth-{Guid.NewGuid():N}.db");
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

        // ---------- session metadata CRUD ----------

        [Test]
        public void CreateWithStart_CreatesSingularStartAndInitialLayout()
        {
            SessionRepository.CreateWithStart(_connection, "s1", "Alpha", "type-standard");

            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, "s1");
            Assert.AreEqual(1, graph.Nodes.Count);
            Assert.IsInstanceOf<SessionStartNodeDefinition>(graph.Nodes[0]);
            var layout = AuthoringLayoutRepository.LoadSessionNodePositions(_connection, "s1");
            Assert.AreEqual(0, layout[graph.Nodes[0].Id].X, 0.001);
            Assert.AreEqual(0, layout[graph.Nodes[0].Id].Y, 0.001);
            Assert.Throws<InvalidOperationException>(() => SessionGraphRepository.AddNode(
                _connection, "s1", new SessionStartNodeDefinition { Id = "another-start" }));
        }

        [Test]
        public void CreateRenameSetType_AndLoaderRoundTrip()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            SessionRepository.Rename(_connection, "s1", "Alpha Prime");
            SessionRepository.SetSessionType(_connection, "s1", "type-marathon");

            var content = GameContentSnapshotLoader.Load(_connection);
            Assert.AreEqual(1, content.Sessions.Count);
            Assert.AreEqual("Alpha Prime", content.Sessions[0].Title);
            Assert.AreEqual("type-marathon", content.Sessions[0].SessionTypeId);
        }

        [Test]
        public void DeleteSession_CascadesAuthoredGraph()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "n-start-out", Kind = GraphPortKind.Normal });
            SessionGraphRepository.AddNode(_connection, "s1", start);
            var end = new SessionEndNodeDefinition { Id = "n-end" };
            SessionGraphRepository.AddNode(_connection, "s1", end);
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = "n-start-out", TargetNodeId = "n-end",
            });
            AuthoringLayoutRepository.SaveSessionNodePosition(_connection, "s1", "n-start", 10, 20);

            SessionRepository.Delete(_connection, "s1");

            Assert.AreEqual(0, Count("session_graph_node"));
            Assert.AreEqual(0, Count("session_graph_edge"));
            Assert.AreEqual(0, Count("session_node_output"));
            Assert.AreEqual(0, Count("wpf_session_node_layout"));
            Assert.AreEqual(0, Count("session"));
        }

        // ---------- node authoring ----------

        [Test]
        public void AddStart_PhaseRef_End_PersistsAndRoundTrips()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            Execute("INSERT INTO phase VALUES ('p1','Phase',0,0);");
            Execute("INSERT INTO phase_exit VALUES ('px-c','p1',0,'Complete');");
            Execute("INSERT INTO phase_exit VALUES ('px-f','p1',1,'Fail');");

            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "n-start-out", Kind = GraphPortKind.Normal });
            SessionGraphRepository.AddNode(_connection, "s1", start);

            var reference = new PhaseReferenceNodeDefinition { Id = "n-ref", PhaseId = "p1" };
            reference.Outputs.Add(new GraphOutputDefinition { Id = "n-ref-exit-px-c", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-c" });
            reference.Outputs.Add(new GraphOutputDefinition { Id = "n-ref-exit-px-f", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-f" });
            SessionGraphRepository.AddNode(_connection, "s1", reference);

            SessionGraphRepository.AddNode(_connection, "s1", new SessionEndNodeDefinition { Id = "n-end" });

            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, "s1");
            Assert.AreEqual(3, graph.Nodes.Count);
            Assert.IsInstanceOf<SessionStartNodeDefinition>(graph.Nodes.Find(n => n.Id == "n-start"));
            Assert.IsInstanceOf<PhaseReferenceNodeDefinition>(graph.Nodes.Find(n => n.Id == "n-ref"));
            Assert.IsInstanceOf<SessionEndNodeDefinition>(graph.Nodes.Find(n => n.Id == "n-end"));

            var phaseRef = (PhaseReferenceNodeDefinition)graph.Nodes.Find(n => n.Id == "n-ref");
            Assert.AreEqual("p1", phaseRef.PhaseId);
            Assert.AreEqual(2, phaseRef.Outputs.Count);
            Assert.AreEqual(GraphPortKind.PhaseExit, phaseRef.Outputs[0].Kind);
            Assert.AreEqual("px-c", phaseRef.Outputs[0].PhaseExitId);
        }

        [Test]
        public void RemoveNonStructuralNode_CascadesItsOutputsAndEdges()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var decision = new SessionDecisionNodeDefinition { Id = "n-decision", Prompt = "Choose" };
            decision.Outputs.Add(new GraphOutputDefinition { Id = "n-decision-out", Kind = GraphPortKind.Normal });
            SessionGraphRepository.AddNode(_connection, "s1", decision);
            SessionGraphRepository.AddNode(_connection, "s1", new SessionEndNodeDefinition { Id = "n-end" });
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = "n-decision-out", TargetNodeId = "n-end",
            });

            SessionGraphRepository.RemoveNode(_connection, "s1", "n-decision");

            Assert.AreEqual(1, Count("session_graph_node"));
            Assert.AreEqual(0, Count("session_graph_edge"));
            Assert.AreEqual(0, Count("session_node_output"));
        }

        [Test]
        public void RemoveStart_IsRejected_ByStructuralInvariant()
        {
            SessionRepository.CreateWithStart(_connection, "s1", "Alpha", "type-standard");
            var start = GameContentSnapshotLoader.LoadSessionGraph(_connection, "s1").Nodes[0];
            Assert.Throws<InvalidOperationException>(() => SessionGraphRepository.RemoveNode(_connection, "s1", start.Id));
            Assert.AreEqual(1, Count("session_graph_node"));
        }

        [Test]
        public void AddEdge_WithSameSourceTwice_IsRejected_ByUniqueConstraint()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "n-start-out", Kind = GraphPortKind.Normal });
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionGraphRepository.AddNode(_connection, "s1", new SessionEndNodeDefinition { Id = "n-end-1" });
            SessionGraphRepository.AddNode(_connection, "s1", new SessionEndNodeDefinition { Id = "n-end-2" });

            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = "n-start-out", TargetNodeId = "n-end-1",
            });
            var failure = Assert.Throws<SqliteException>(() => SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e2", SourceOutputId = "n-start-out", TargetNodeId = "n-end-2",
            }));
            StringAssert.Contains("UNIQUE", failure.Message.ToUpperInvariant());
        }

        [Test]
        public void RemoveEdgesFromSource_DisconnectsPersistedEdge()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "n-start-out", Kind = GraphPortKind.Normal });
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionGraphRepository.AddNode(_connection, "s1", new SessionEndNodeDefinition { Id = "n-end" });
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = "n-start-out", TargetNodeId = "n-end",
            });

            SessionGraphRepository.RemoveEdgesFromSource(_connection, "n-start-out");

            Assert.AreEqual(0, Count("session_graph_edge"));
        }

        // ---------- layout persistence ----------

        [Test]
        public void NodeLayout_SavesLoadsAndOverwrites()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            SessionGraphRepository.AddNode(_connection, "s1", new SessionStartNodeDefinition { Id = "n-start" });

            AuthoringLayoutRepository.SaveSessionNodePosition(_connection, "s1", "n-start", 10, 20);
            AuthoringLayoutRepository.SaveSessionNodePosition(_connection, "s1", "n-start", 130, 240);

            var layout = AuthoringLayoutRepository.LoadSessionNodePositions(_connection, "s1");
            Assert.AreEqual(1, layout.Count);
            Assert.AreEqual(130, layout["n-start"].X, 0.001);
            Assert.AreEqual(240, layout["n-start"].Y, 0.001);
        }

        [Test]
        public void Viewport_SavesAndLoads()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            AuthoringLayoutRepository.SaveViewport(_connection, "session", "s1", 0.75, -120, 60);

            var viewport = AuthoringLayoutRepository.LoadViewport(_connection, "session", "s1");
            Assert.IsTrue(viewport.HasValue);
            Assert.AreEqual(0.75, viewport.Value.Zoom, 0.001);
            Assert.AreEqual(-120, viewport.Value.X, 0.001);
            Assert.AreEqual(60, viewport.Value.Y, 0.001);
        }

        [Test]
        public void LoadViewport_ReturnsNull_WhenNoRowExists()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            Assert.IsFalse(AuthoringLayoutRepository.LoadViewport(_connection, "session", "s1").HasValue);
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
