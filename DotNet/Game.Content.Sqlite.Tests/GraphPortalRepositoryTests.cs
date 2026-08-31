using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    [TestFixture]
    public sealed class GraphPortalRepositoryTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "sqlite-portals-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);

            Execute("INSERT INTO session_type (id, title) VALUES ('type', 'Type');");
            Execute("INSERT INTO session (id, title, session_type_id) VALUES ('session', 'Session', 'type');");
            Execute("INSERT INTO session_graph_node (id, session_id, node_type) VALUES ('s-source', 'session', 'Start');");
            Execute("INSERT INTO session_graph_node (id, session_id, node_type) VALUES ('s-target', 'session', 'End');");
            Execute("INSERT INTO session_node_output (id, node_id, port_kind, ordinal) VALUES ('s-output', 's-source', 'Normal', 0);");
            Execute("INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) VALUES ('s-edge', 'session', 's-output', 's-target');");

            Execute("INSERT INTO phase (id, title, min_cards, max_cards) VALUES ('phase', 'Phase', 0, 0);");
            Execute("INSERT INTO phase_graph_node (id, phase_id, node_type) VALUES ('p-source', 'phase', 'Entry');");
            Execute("INSERT INTO phase_graph_node (id, phase_id, node_type) VALUES ('p-target', 'phase', 'Return');");
            Execute("INSERT INTO phase_node_output (id, node_id, port_kind, ordinal) VALUES ('p-output', 'p-source', 'Normal', 0);");
            Execute("INSERT INTO phase_graph_edge (id, phase_id, source_port_id, target_node_id) VALUES ('p-edge', 'phase', 'p-output', 'p-target');");
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void SessionAndPhasePairs_CrudAndEndpointMovesRoundTrip()
        {
            GraphPortalRepository.Create(_connection, "session", Pair("sp", "session", "s-edge"));
            GraphPortalRepository.Create(_connection, "phase", Pair("pp", "phase", "p-edge"));

            GraphPortalRepository.UpdateSource(_connection, "session", "sp", 125, 250);
            GraphPortalRepository.UpdateTarget(_connection, "session", "sp", 425, 450);
            var session = GraphPortalRepository.LoadSession(_connection, "session").Single();
            var phase = GraphPortalRepository.GetByEdge(_connection, "phase", "phase", "p-edge");

            Assert.That(session.SourceX, Is.EqualTo(125));
            Assert.That(session.TargetY, Is.EqualTo(450));
            Assert.That(session.Label, Is.EqualTo("Portal"));
            Assert.That(phase.Id, Is.EqualTo("pp"));
            Assert.That(phase.GraphId, Is.EqualTo("phase"));
        }

        [Test]
        public void OwnerTrigger_RejectsPortalForAnEdgeFromAnotherGraph()
        {
            Assert.Throws<SqliteException>(() => Execute(
                "INSERT INTO wpf_session_edge_portal_pair " +
                "(id, session_id, edge_id, label, color_slot, source_x, source_y, target_x, target_y) " +
                "VALUES ('bad', 'session', 'p-edge', 'Bad', 0, 0, 0, 1, 1);"));
        }

        [Test]
        public void GraphDelete_CascadesItsPortalMetadata()
        {
            GraphPortalRepository.Create(_connection, "session", Pair("sp", "session", "s-edge"));
            Execute("DELETE FROM session WHERE id = 'session';");

            Assert.That(GraphPortalRepository.LoadSession(_connection, "session"), Is.Empty);
        }

        [Test]
        public void InvalidCoordinatesAndGraphKindsFailBeforeWrite()
        {
            var invalid = Pair("bad", "session", "s-edge");
            invalid.SourceX = double.NaN;
            Assert.Throws<InvalidOperationException>(() => GraphPortalRepository.Create(_connection, "session", invalid));
            Assert.Throws<ArgumentException>(() => GraphPortalRepository.Create(_connection, "other", Pair("other", "session", "s-edge")));
        }

        private static GraphPortalPairDefinition Pair(string id, string graphId, string edgeId)
        {
            return new GraphPortalPairDefinition
            {
                Id = id, GraphId = graphId, EdgeId = edgeId, Label = "Portal", ColorSlot = 2,
                SourceX = 25, SourceY = 50, TargetX = 75, TargetY = 50,
            };
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
