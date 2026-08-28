using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 18: the semantic undo/redo command layer. Every command executes
    /// and undoes through explicit repositories; coalesced commands (moves,
    /// text edits) collapse into one history entry that undoes to the original.
    /// </summary>
    [TestFixture]
    public class AuthoringCommandTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-cmd-{Guid.NewGuid():N}.db");
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

        // Commands dispose the connection they open (like the app), so the
        // factory must open a fresh connection per call against the same file.
        private Func<DbConnection> Conn => () =>
        {
            var connection = new SqliteConnection("Data Source=" + _dbPath);
            connection.Open();
            ConnectionInitializer.Initialize(connection);
            return connection;
        };

        // ---------- coalescing ----------

        [Test]
        public void MoveNode_MergesFrames_UndoRestoresOriginal()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var node = new SessionStartNodeDefinition { Id = "s1-start" };
            node.Outputs.Add(new GraphOutputDefinition { Id = "s1-start-out", Kind = GraphPortKind.Normal });
            SessionGraphRepository.AddNode(_connection, "s1", node);
            AuthoringLayoutRepository.SaveSessionNodePosition(_connection, "s1", node.Id, 10, 20);

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new MoveNodeCommand(Conn, "session", "s1", node.Id, new Point2(10, 20), new Point2(100, 200)));
            stack.PushOrMerge(new MoveNodeCommand(Conn, "session", "s1", node.Id, new Point2(10, 20), new Point2(300, 400)));

            Assert.AreEqual(1, stack.UndoCount, "drag frames coalesce into one command");

            stack.Undo();
            var pos = AuthoringLayoutRepository.GetSessionNodePosition(_connection, "s1", node.Id);
            Assert.AreEqual(10, pos.Value.X);
            Assert.AreEqual(20, pos.Value.Y);

            stack.Redo();
            pos = AuthoringLayoutRepository.GetSessionNodePosition(_connection, "s1", node.Id);
            Assert.AreEqual(300, pos.Value.X);
            Assert.AreEqual(400, pos.Value.Y);
        }

        [Test]
        public void PromptEdit_Coalesces_UndoRestoresOriginal()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var session = new SessionDefinition { Id = "s1", Title = "Alpha", SessionTypeId = "type-standard" };
            var decision = new SessionDecisionNodeDefinition { Id = "d1", Prompt = "Go?" };
            session.Graph.Nodes.Add(decision);
            SessionGraphRepository.AddNode(_connection, "s1", decision);

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new SetDecisionPromptCommand(Conn, "session", "d1", "Go?", "G"));
            stack.PushOrMerge(new SetDecisionPromptCommand(Conn, "session", "d1", "Go?", "Go left?"));

            Assert.AreEqual(1, stack.UndoCount);
            Assert.AreEqual("Go left?", AuthoringUndo.GetDecisionPrompt(_connection, "session", "d1"));

            stack.Undo();
            Assert.AreEqual("Go?", AuthoringUndo.GetDecisionPrompt(_connection, "session", "d1"));
        }

        // ---------- node add/delete ----------

        [Test]
        public void AddNode_UndoRedo_RoundTrips()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var node = new SessionDecisionNodeDefinition { Id = "d1", Prompt = "Choose…" };
            node.Outputs.Add(new GraphOutputDefinition { Id = "d1-out", Kind = GraphPortKind.Normal });

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new AddSessionNodeCommand(Conn, "s1", node, new Point2(40, 50)));
            Assert.AreEqual(1, Count("session_graph_node WHERE id='d1'"));

            stack.Undo();
            Assert.AreEqual(0, Count("session_graph_node WHERE id='d1'"));
            Assert.AreEqual(0, Count("wpf_session_node_layout WHERE node_id='d1'"));

            stack.Redo();
            Assert.AreEqual(1, Count("session_graph_node WHERE id='d1'"));
            Assert.AreEqual(1, Count("wpf_session_node_layout WHERE node_id='d1'"));
        }

        [Test]
        public void DeleteNode_WithEdges_UndoRestoresEverything()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var start = new SessionStartNodeDefinition { Id = "s1-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "s1-start-out", Kind = GraphPortKind.Normal });
            var end = new SessionEndNodeDefinition { Id = "s1-end" };
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionGraphRepository.AddNode(_connection, "s1", end);
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = "s1-start-out", TargetNodeId = "s1-end",
            });
            AuthoringLayoutRepository.SaveSessionNodePosition(_connection, "s1", "s1-end", 5, 6);

            var touching = AuthoringUndo.SessionEdgesTouchingNode(_connection, "s1", "s1-end");
            Assert.AreEqual(1, touching.Count, "edge into the deleted node is captured");

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new DeleteSessionNodeCommand(Conn, "s1", end, touching, new Point2(5, 6)));
            Assert.AreEqual(0, Count("session_graph_node WHERE id='s1-end'"));
            Assert.AreEqual(0, Count("session_graph_edge WHERE id='e1'"), "cascade removed the edge");

            stack.Undo();
            Assert.AreEqual(1, Count("session_graph_node WHERE id='s1-end'"));
            Assert.AreEqual(1, Count("session_graph_edge WHERE source_port_id='s1-start-out'"));
            Assert.AreEqual(1, Count("wpf_session_node_layout WHERE node_id='s1-end'"));

            stack.Redo();
            Assert.AreEqual(0, Count("session_graph_node WHERE id='s1-end'"));
        }

        // ---------- connect/disconnect ----------

        [Test]
        public void Connect_ReplacesEdge_UndoRestoresReplacedEdge()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var start = new SessionStartNodeDefinition { Id = "s1-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "s1-start-out", Kind = GraphPortKind.Normal });
            var a = new SessionEndNodeDefinition { Id = "s1-a" };
            var b = new SessionEndNodeDefinition { Id = "s1-b" };
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionGraphRepository.AddNode(_connection, "s1", a);
            SessionGraphRepository.AddNode(_connection, "s1", b);
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = "s1-start-out", TargetNodeId = "s1-a",
            });

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new ConnectSessionCommand(Conn, "s1", "s1-start-out", "s1-b", "s1-a"));
            Assert.AreEqual("s1-b", Scalar("SELECT target_node_id FROM session_graph_edge WHERE source_port_id='s1-start-out'"));

            stack.Undo();
            Assert.AreEqual("s1-a", Scalar("SELECT target_node_id FROM session_graph_edge WHERE source_port_id='s1-start-out'"),
                "the replaced edge is restored");

            stack.Redo();
            Assert.AreEqual("s1-b", Scalar("SELECT target_node_id FROM session_graph_edge WHERE source_port_id='s1-start-out'"));
        }

        [Test]
        public void Disconnect_UndoRewires()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var start = new SessionStartNodeDefinition { Id = "s1-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "s1-start-out", Kind = GraphPortKind.Normal });
            var end = new SessionEndNodeDefinition { Id = "s1-end" };
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionGraphRepository.AddNode(_connection, "s1", end);
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = "s1-start-out", TargetNodeId = "s1-end",
            });

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new DisconnectSessionCommand(Conn, "s1", "s1-start-out", "s1-end"));
            Assert.AreEqual(0, Count("session_graph_edge WHERE source_port_id='s1-start-out'"));

            stack.Undo();
            Assert.AreEqual(1, Count("session_graph_edge WHERE source_port_id='s1-start-out'"));
        }

        // ---------- action instances ----------

        [Test]
        public void PhaseGoto_AddRemove_UndoRestoresExactOrdinal()
        {
            PhaseRepository.Create(_connection, "p1", "Cold");
            var phase = new PhaseDefinition { Id = "p1", Title = "Cold" };
            phase.Exits.Add(new PhaseExitDefinition { Id = "px-a", Name = "A" });
            var action = new ActionNodeDefinition { Id = "n-a", Sequence = new ActionSequenceDefinition { Id = "n-a-seq" } };
            action.Outputs.Add(new GraphOutputDefinition { Id = "n-a-out", Kind = GraphPortKind.Normal });
            phase.Graph.Nodes.Add(action);
            PhaseGraphRepository.AddNode(_connection, "p1", action);
            PhaseExitRepository.Create(_connection, "p1", phase.Exits[0], 0);

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new AddPhaseGotoCommand(Conn, "n-a", "px-a"));
            Assert.AreEqual(1, Count("action_instance_phase_goto WHERE phase_exit_id='px-a'"));

            // Remove it, then undo: the instance must come back at its exact ordinal.
            var snapshot = AuthoringUndo.SnapshotPhaseGoto(_connection, "n-a-goto-0");
            stack.PushOrMerge(new RemovePhaseGotoCommand(Conn, snapshot));
            Assert.AreEqual(0, Count("action_instance_phase_goto"));

            stack.Undo();
            Assert.AreEqual(1, Count("action_instance_phase_goto WHERE action_instance_id='n-a-goto-0' AND phase_exit_id='px-a'"));
            Assert.AreEqual("0", Scalar("SELECT ordinal FROM action_instance WHERE id='n-a-goto-0'").ToString());
        }

        [Test]
        public void SessionGoto_RemoveUndo_RestoresInstancePortAndEdge()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var session = new SessionDefinition { Id = "s1", Title = "Alpha", SessionTypeId = "type-standard" };
            var decision = new SessionDecisionNodeDefinition { Id = "d1", Prompt = "Go?" };
            session.Graph.Nodes.Add(decision);
            var start = new SessionStartNodeDefinition { Id = "s1-start" };
            session.Graph.Nodes.Add(start);
            SessionGraphRepository.AddNode(_connection, "s1", decision);
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionDecisionRepository.AddOption(_connection, "d1", "d1-opt-1", "Yes", "d1-opt-1-seq");
            var instanceId = SessionDecisionRepository.AddSessionGoto(_connection, "d1", "d1-opt-1", "leave");
            var portId = instanceId + "-port";
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = portId, TargetNodeId = "s1-start",
            });
            Assert.AreEqual(1, Count("session_graph_edge WHERE source_port_id='" + portId + "'"));

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new RemoveSessionGotoCommand(Conn, AuthoringUndo.SnapshotSessionGoto(_connection, instanceId)));
            Assert.AreEqual(0, Count("action_instance WHERE id='" + instanceId + "'"));
            Assert.AreEqual(0, Count("session_node_output WHERE id='" + portId + "'"));
            Assert.AreEqual(0, Count("session_graph_edge WHERE source_port_id='" + portId + "'"));

            stack.Undo();
            Assert.AreEqual(1, Count("action_instance_session_goto WHERE action_instance_id='" + instanceId + "' AND label='leave'"));
            Assert.AreEqual(1, Count("session_node_output WHERE id='" + portId + "'"));
            Assert.AreEqual(1, Count("session_graph_edge WHERE source_port_id='" + portId + "' AND target_node_id='s1-start'"));
        }

        [Test]
        public void SetPhaseGotoExit_RoundTrips()
        {
            PhaseRepository.Create(_connection, "p1", "Cold");
            var phase = new PhaseDefinition { Id = "p1", Title = "Cold" };
            phase.Exits.Add(new PhaseExitDefinition { Id = "px-a", Name = "A" });
            phase.Exits.Add(new PhaseExitDefinition { Id = "px-b", Name = "B" });
            PhaseExitRepository.Create(_connection, "p1", phase.Exits[0], 0);
            PhaseExitRepository.Create(_connection, "p1", phase.Exits[1], 1);
            var action = new ActionNodeDefinition { Id = "n-a", Sequence = new ActionSequenceDefinition { Id = "n-a-seq" } };
            PhaseGraphRepository.AddNode(_connection, "p1", action);
            PhaseGraphRepository.AddPhaseGoto(_connection, "n-a", "px-a");

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new SetPhaseGotoExitCommand(Conn, "n-a-goto-0", "px-a", "px-b"));
            Assert.AreEqual("px-b", AuthoringUndo.GetPhaseGotoExit(_connection, "n-a-goto-0"));

            stack.Undo();
            Assert.AreEqual("px-a", AuthoringUndo.GetPhaseGotoExit(_connection, "n-a-goto-0"));
        }

        // ---------- exits ----------

        [Test]
        public void CreateExit_UndoDeletesProjectedSockets()
        {
            var phase = new PhaseDefinition { Id = "p1", Title = "Cold" };
            ReuseWriter.WriteClonedPhase(_connection, phase);
            // One placement projecting the (currently empty) exit set.
            var session = BuildPlacementSession();
            ReuseWriter.WriteClonedSession(_connection, session);

            var exit = new PhaseExitDefinition { Id = "px-new", Name = "Bonus" };
            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new CreateExitCommand(Conn, "p1", exit, 0));
            Assert.AreEqual(1, Count("phase_exit WHERE id='px-new'"));
            Assert.AreEqual(1, Count("session_node_output WHERE phase_exit_id='px-new'"), "placement projects the new exit");

            stack.Undo();
            Assert.AreEqual(0, Count("phase_exit WHERE id='px-new'"));
            Assert.AreEqual(0, Count("session_node_output WHERE phase_exit_id='px-new'"), "projected socket cascaded away");
        }

        [Test]
        public void DeleteExit_UndoRestoresExitSocketAndEdge()
        {
            var phase = new PhaseDefinition { Id = "p1", Title = "Cold" };
            phase.Exits.Add(new PhaseExitDefinition { Id = "px-c", Name = "Complete" });
            ReuseWriter.WriteClonedPhase(_connection, phase);
            var session = BuildPlacementSession();
            ReuseWriter.WriteClonedSession(_connection, session);
            PhaseExitRepository.SyncProjectedSockets(_connection, "p1", "px-c", 0);
            // Wire the projected socket.
            var socketId = Scalar("SELECT id FROM session_node_output WHERE phase_exit_id='px-c'").ToString();
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = socketId, TargetNodeId = "s1-start",
            });
            Assert.AreEqual(1, Count("session_graph_edge WHERE source_port_id='" + socketId + "'"));

            var projectedEdges = AuthoringUndo.ExitProjectedEdges(_connection, "px-c");
            Assert.AreEqual(1, projectedEdges.Count);

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new DeleteExitCommand(Conn, "p1", phase.Exits[0], 0, projectedEdges));
            Assert.AreEqual(0, Count("phase_exit WHERE id='px-c'"));
            Assert.AreEqual(0, Count("session_graph_edge WHERE source_port_id='" + socketId + "'"));

            stack.Undo();
            Assert.AreEqual(1, Count("phase_exit WHERE id='px-c'"));
            Assert.AreEqual(1, Count("session_node_output WHERE phase_exit_id='px-c'"));
            Assert.AreEqual(1, Count("session_graph_edge WHERE source_port_id='" + socketId + "' AND target_node_id='s1-start'"),
                "the edge wired from the projected socket is restored");
        }

        // ---------- decision options ----------

        [Test]
        public void RemoveOptionWithGotos_UndoRestoresEverything()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var session = new SessionDefinition { Id = "s1", Title = "Alpha", SessionTypeId = "type-standard" };
            var decision = new SessionDecisionNodeDefinition { Id = "d1", Prompt = "Go?" };
            session.Graph.Nodes.Add(decision);
            var start = new SessionStartNodeDefinition { Id = "s1-start" };
            session.Graph.Nodes.Add(start);
            SessionGraphRepository.AddNode(_connection, "s1", decision);
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionDecisionRepository.AddOption(_connection, "d1", "d1-opt-1", "Yes", "d1-opt-1-seq");
            var instanceId = SessionDecisionRepository.AddSessionGoto(_connection, "d1", "d1-opt-1", "leave");
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = instanceId + "-port", TargetNodeId = "s1-start",
            });

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new RemoveDecisionOptionCommand(Conn, "session",
                AuthoringUndo.SnapshotSessionOption(_connection, "d1", "d1-opt-1")));
            Assert.AreEqual(0, Count("session_decision_option WHERE id='d1-opt-1'"));
            Assert.AreEqual(0, Count("action_instance WHERE id='" + instanceId + "'"));

            stack.Undo();
            Assert.AreEqual(1, Count("session_decision_option WHERE id='d1-opt-1' AND label='Yes'"));
            Assert.AreEqual(1, Count("session_node_output WHERE session_goto_action_instance_id='" + instanceId + "'"));
            Assert.AreEqual(1, Count("session_graph_edge WHERE source_port_id='" + instanceId + "-port' AND target_node_id='s1-start'"));
        }

        // ---------- reuse operations ----------

        [Test]
        public void CopySession_UndoDeletesCopy()
        {
            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            var source = new SessionDefinition { Id = "s1", Title = "Alpha", SessionTypeId = "type-standard" };
            var start = new SessionStartNodeDefinition { Id = "s1-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "s1-start-out", Kind = GraphPortKind.Normal });
            source.Graph.Nodes.Add(start);
            SessionGraphRepository.AddNode(_connection, "s1", start);

            var clone = ContentCloner.CloneSession(source, "s-copy", "Alpha (copy)");
            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new CopySessionCommand(Conn, clone.Session, new List<(string, double, double)>()));
            Assert.AreEqual(1, Count("session WHERE id='s-copy'"));

            stack.Undo();
            Assert.AreEqual(0, Count("session WHERE id='s-copy'"));
            Assert.AreEqual(0, Count("session_graph_node WHERE session_id='s-copy'"));

            stack.Redo();
            Assert.AreEqual(1, Count("session WHERE id='s-copy'"));
        }

        [Test]
        public void MakeUnique_UndoRestoresPlacementAndDeletesClone()
        {
            var shared = new PhaseDefinition { Id = "p1", Title = "Cold" };
            shared.Exits.Add(new PhaseExitDefinition { Id = "px-c", Name = "Complete" });
            ReuseWriter.WriteClonedPhase(_connection, shared);
            var session = BuildPlacementSession();
            ReuseWriter.WriteClonedSession(_connection, session);
            PhaseExitRepository.SyncProjectedSockets(_connection, "p1", "px-c", 0);

            var portMap = AuthoringUndo.PlacementExitMap(_connection, "s1-ref");
            Assert.AreEqual("px-c", portMap[Scalar("SELECT id FROM session_node_output WHERE node_id='s1-ref' AND phase_exit_id IS NOT NULL").ToString()]);

            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new MakeUniqueCommand(Conn, shared, "s1-ref", "p-unique", portMap));
            Assert.AreEqual("p-unique", Scalar("SELECT phase_id FROM session_node_phase WHERE node_id='s1-ref'").ToString());

            stack.Undo();
            Assert.AreEqual("p1", Scalar("SELECT phase_id FROM session_node_phase WHERE node_id='s1-ref'").ToString());
            Assert.AreEqual(0, Count("phase WHERE id='p-unique'"), "the clone is removed");
            var restoredPort = Scalar("SELECT id FROM session_node_output WHERE node_id='s1-ref' AND phase_exit_id IS NOT NULL").ToString();
            Assert.AreEqual("px-c", Scalar("SELECT phase_exit_id FROM session_node_output WHERE id='" + restoredPort + "'").ToString(),
                "projected exit mapping restored to the shared phase");

            stack.Redo();
            Assert.AreEqual("p-unique", Scalar("SELECT phase_id FROM session_node_phase WHERE node_id='s1-ref'").ToString());
        }

        [Test]
        public void Composite_MakeUniquePlusAddExit_UndoesAsOneStep()
        {
            var shared = new PhaseDefinition { Id = "p1", Title = "Cold" };
            shared.Exits.Add(new PhaseExitDefinition { Id = "px-c", Name = "Complete" });
            ReuseWriter.WriteClonedPhase(_connection, shared);
            var session = BuildPlacementSession();
            ReuseWriter.WriteClonedSession(_connection, session);
            PhaseExitRepository.SyncProjectedSockets(_connection, "p1", "px-c", 0);

            var portMap = AuthoringUndo.PlacementExitMap(_connection, "s1-ref");
            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new CompositeCommand("Make unique + add exit",
                new MakeUniqueCommand(Conn, shared, "s1-ref", "p-unique", portMap),
                new CreateExitCommand(Conn, "p-unique", new PhaseExitDefinition { Id = "px-new", Name = "Bonus" }, -1)));

            Assert.AreEqual(1, stack.UndoCount, "the pair is ONE history entry");
            Assert.AreEqual(1, Count("phase_exit WHERE phase_id='p-unique' AND id='px-new'"));
            Assert.AreEqual(2, Count("phase_exit WHERE phase_id='p-unique'"));

            stack.Undo();
            Assert.AreEqual("p1", Scalar("SELECT phase_id FROM session_node_phase WHERE node_id='s1-ref'").ToString());
            Assert.AreEqual(0, Count("phase WHERE id='p-unique'"), "clone removed in the same undo");
            Assert.AreEqual(1, Count("phase_exit WHERE id='px-c'"), "shared phase untouched");
        }

        // ---------- helpers ----------

        private SessionDefinition BuildPlacementSession()
        {
            var session = new SessionDefinition { Id = "s1", Title = "Alpha", SessionTypeId = "type-standard" };
            var start = new SessionStartNodeDefinition { Id = "s1-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "s1-start-out", Kind = GraphPortKind.Normal });
            session.Graph.Nodes.Add(start);
            var reference = new PhaseReferenceNodeDefinition { Id = "s1-ref", PhaseId = "p1" };
            session.Graph.Nodes.Add(reference);
            return session;
        }

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
    }
}
