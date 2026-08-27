using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 16: decision action lists + SessionGoto dynamic sockets.
    /// Adding a SessionGoto creates instance + unique port in one transaction;
    /// deleting it removes the port and any wired edge; identical labels stay
    /// distinct sockets. PhaseGoto in PhaseDecision options uses the shared
    /// exit dropdown and never creates a decision output.
    /// </summary>
    [TestFixture]
    public class DecisionAuthoringTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-decision-{Guid.NewGuid():N}.db");
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

        private string CreateSessionWithDecision(out string nodeId)
        {
            var sessionId = "s1";
            SessionRepository.Create(_connection, sessionId, "Alpha", "type-standard");
            nodeId = "sn-s1-decision-1";
            Execute("INSERT INTO session_graph_node VALUES ('" + nodeId + "','" + sessionId + "','Decision');");
            Execute("INSERT INTO session_node_decision VALUES ('" + nodeId + "','Choose');");
            return sessionId;
        }

        // ---------- decision options ----------

        [Test]
        public void AddOption_RoundTripsThroughLoader()
        {
            var sessionId = CreateSessionWithDecision(out var nodeId);
            SessionDecisionRepository.AddOption(_connection, nodeId, nodeId + "-opt-1", "Option A", nodeId + "-opt-1-seq");

            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, sessionId);
            var decision = (SessionDecisionNodeDefinition)graph.Nodes.Find(n => n.Id == nodeId);
            Assert.AreEqual(1, decision.Options.Count);
            Assert.AreEqual("Option A", decision.Options[0].Label);
        }

        [Test]
        public void PromptAndLabelEdits_Persist()
        {
            var sessionId = CreateSessionWithDecision(out var nodeId);
            SessionDecisionRepository.AddOption(_connection, nodeId, nodeId + "-opt-1", "Old", nodeId + "-opt-1-seq");
            SessionDecisionRepository.UpdatePrompt(_connection, nodeId, "Choose wisely");
            SessionDecisionRepository.RenameOption(_connection, nodeId + "-opt-1", "Renamed");

            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, sessionId);
            var decision = (SessionDecisionNodeDefinition)graph.Nodes.Find(n => n.Id == nodeId);
            Assert.AreEqual("Choose wisely", decision.Prompt);
            Assert.AreEqual("Renamed", decision.Options[0].Label);
        }

        [Test]
        public void RemoveOption_DeletesRow_AndItsSequence()
        {
            var sessionId = CreateSessionWithDecision(out var nodeId);
            SessionDecisionRepository.AddOption(_connection, nodeId, nodeId + "-opt-1", "A", nodeId + "-opt-1-seq");
            SessionDecisionRepository.RemoveOption(_connection, nodeId, nodeId + "-opt-1");

            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, sessionId);
            var decision = (SessionDecisionNodeDefinition)graph.Nodes.Find(n => n.Id == nodeId);
            Assert.AreEqual(0, decision.Options.Count);
            Assert.AreEqual(0, Count("session_decision_option"));
        }

        // ---------- SessionGoto dynamic sockets ----------

        [Test]
        public void AddSessionGoto_CreatesInstanceAndUniquePort_Atomically()
        {
            var sessionId = CreateSessionWithDecision(out var nodeId);
            SessionDecisionRepository.AddOption(_connection, nodeId, nodeId + "-opt-1", "A", nodeId + "-opt-1-seq");

            var instanceId = SessionDecisionRepository.AddSessionGoto(_connection, nodeId, nodeId + "-opt-1", "to end");

            // Instance + subtype + unique port all present.
            Assert.AreEqual(1, Count("action_instance_session_goto"));
            Assert.AreEqual(1, Count("session_node_output WHERE port_kind='session_goto'"));
            Assert.AreEqual(instanceId,
                Scalar("SELECT session_goto_action_instance_id FROM session_node_output WHERE port_kind='session_goto'").ToString());

            // Loader round-trip: decision option owns the instance; node owns the port.
            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, sessionId);
            var decision = (SessionDecisionNodeDefinition)graph.Nodes.Find(n => n.Id == nodeId);
            Assert.AreEqual(1, decision.Options[0].Sequence.Instances.Count);
            var gotoInstance = (SessionGotoInstanceDefinition)decision.Options[0].Sequence.Instances[0];
            Assert.AreEqual("to end", gotoInstance.Label);
            Assert.IsTrue(gotoInstance.IsBlocking);
            Assert.AreEqual(instanceId, gotoInstance.Id);

            var port = decision.Outputs.Find(o => o.Kind == GraphPortKind.SessionGoto);
            Assert.IsNotNull(port, "unique port on the decision node");
            Assert.AreEqual(instanceId, port.SessionGotoActionInstanceId);
        }

        [Test]
        public void SameLabel_IsTwoDistinctSockets()
        {
            var sessionId = CreateSessionWithDecision(out var nodeId);
            SessionDecisionRepository.AddOption(_connection, nodeId, nodeId + "-opt-1", "A", nodeId + "-opt-1-seq");

            var instanceA = SessionDecisionRepository.AddSessionGoto(_connection, nodeId, nodeId + "-opt-1", "same");
            var instanceB = SessionDecisionRepository.AddSessionGoto(_connection, nodeId, nodeId + "-opt-1", "same");

            Assert.AreNotEqual(instanceA, instanceB);
            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, sessionId);
            var decision = (SessionDecisionNodeDefinition)graph.Nodes.Find(n => n.Id == nodeId);
            var gotoPorts = decision.Outputs.FindAll(o => o.Kind == GraphPortKind.SessionGoto);
            Assert.AreEqual(2, gotoPorts.Count, "same label, distinct sockets");
            Assert.AreNotEqual(gotoPorts[0].SessionGotoActionInstanceId, gotoPorts[1].SessionGotoActionInstanceId);
        }

        [Test]
        public void RenameSessionGoto_UpdatesInstanceAndPortLabel_Live()
        {
            var sessionId = CreateSessionWithDecision(out var nodeId);
            SessionDecisionRepository.AddOption(_connection, nodeId, nodeId + "-opt-1", "A", nodeId + "-opt-1-seq");
            var instanceId = SessionDecisionRepository.AddSessionGoto(_connection, nodeId, nodeId + "-opt-1", "old");

            SessionDecisionRepository.SetSessionGotoLabel(_connection, instanceId, "renamed");

            Assert.AreEqual("renamed", Scalar("SELECT label FROM action_instance_session_goto").ToString());
            Assert.AreEqual("renamed", Scalar("SELECT label FROM session_node_output WHERE port_kind='session_goto'").ToString());
        }

        [Test]
        public void RemoveSessionGoto_DeletesPortAndWiredEdge_Transactionally()
        {
            var sessionId = CreateSessionWithDecision(out var nodeId);
            SessionDecisionRepository.AddOption(_connection, nodeId, nodeId + "-opt-1", "A", nodeId + "-opt-1-seq");
            var instanceId = SessionDecisionRepository.AddSessionGoto(_connection, nodeId, nodeId + "-opt-1", "to end");
            Execute("INSERT INTO session_graph_node VALUES ('end','" + sessionId + "','End');");
            var portId = instanceId + "-port";
            Execute("INSERT INTO session_graph_edge VALUES ('se1','" + sessionId + "','" + portId + "','end');");

            SessionDecisionRepository.RemoveSessionGoto(_connection, instanceId);

            Assert.AreEqual(0, Count("action_instance_session_goto"));
            Assert.AreEqual(0, Count("session_node_output WHERE port_kind='session_goto'"));
            Assert.AreEqual(0, Count("session_graph_edge"), "edge wired from the port removed with it");
        }

        // ---------- PhaseDecision options + PhaseGoto ----------

        [Test]
        public void PhaseDecision_AddOption_AndPhaseGotoToOption()
        {
            PhaseRepository.Create(_connection, "p1", "Cold Start");
            Execute("INSERT INTO phase_exit VALUES ('px-c','p1',0,'Complete');");
            var nodeId = "pn-p1-decision-1";
            Execute("INSERT INTO phase_graph_node VALUES ('" + nodeId + "','p1','Decision');");
            Execute("INSERT INTO phase_node_decision VALUES ('" + nodeId + "','Choose');");

            PhaseDecisionRepository.AddOption(_connection, nodeId, nodeId + "-opt-1", "A", nodeId + "-opt-1-seq");
            PhaseGraphRepository.AddPhaseGotoToOption(_connection, nodeId + "-opt-1", "px-c");

            var content = GameContentSnapshotLoader.Load(_connection);
            var decision = (PhaseDecisionNodeDefinition)content.Phases.Find(p => p.Id == "p1")
                .Graph.Nodes.Find(n => n.Id == nodeId);
            Assert.AreEqual(1, decision.Options.Count);
            Assert.AreEqual(1, decision.Options[0].Sequence.Instances.Count);
            var gotoInstance = (PhaseGotoInstanceDefinition)decision.Options[0].Sequence.Instances[0];
            Assert.AreEqual("px-c", gotoInstance.PhaseExitId);

            // PhaseGoto does NOT create a decision output — only the shared normal output exists.
            Assert.AreEqual(0, decision.Outputs.FindAll(o => o.Kind == GraphPortKind.SessionGoto).Count);
        }

        // ---------- helpers ----------

        private object Scalar(string sql)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
        }

        private long Count(string fromClause)
        {
            return Convert.ToInt64(Scalar("SELECT COUNT(*) " +
                (fromClause.TrimStart().StartsWith("FROM", StringComparison.OrdinalIgnoreCase)
                    ? fromClause : "FROM " + fromClause)));
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
