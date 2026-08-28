using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 17: reuse UX. Deep clones give every owned entity a NEW id (shared
    /// Resources/Temperatures/Phase ids stay shared); Make Unique detaches one
    /// placement with its projected ports and edges intact; later edits isolate.
    /// </summary>
    [TestFixture]
    public class ReuseTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-reuse-{Guid.NewGuid():N}.db");
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

        private PhaseDefinition BuildSharedPhase()
        {
            var phase = new PhaseDefinition { Id = "p1", Title = "Cold Start" };
            phase.Exits.Add(new PhaseExitDefinition { Id = "px-c", Name = "Complete" });
            phase.Exits.Add(new PhaseExitDefinition { Id = "px-f", Name = "Fail" });

            var entry = new PhaseEntryNodeDefinition { Id = "n-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = "n-entry-out", Kind = GraphPortKind.Normal });
            phase.Graph.Nodes.Add(entry);

            var action = new ActionNodeDefinition { Id = "n-action", Sequence = new ActionSequenceDefinition { Id = "n-action-seq" } };
            action.Outputs.Add(new GraphOutputDefinition { Id = "n-action-out", Kind = GraphPortKind.Normal });
            action.Sequence.Instances.Add(new PhaseGotoInstanceDefinition { Id = "goto-1", PhaseExitId = "px-c" });
            phase.Graph.Nodes.Add(action);

            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e1", SourceOutputId = "n-entry-out", TargetNodeId = "n-action" });
            return phase;
        }

        private SessionDefinition BuildSharedSession()
        {
            var session = new SessionDefinition { Id = "s1", Title = "Alpha", SessionTypeId = "type-standard" };
            var start = new SessionStartNodeDefinition { Id = "s1-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "s1-start-out", Kind = GraphPortKind.Normal });
            session.Graph.Nodes.Add(start);

            var reference = new PhaseReferenceNodeDefinition { Id = "s1-ref", PhaseId = "p1" };
            reference.Outputs.Add(new GraphOutputDefinition { Id = "s1-ref-exit-px-c", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-c" });
            reference.Outputs.Add(new GraphOutputDefinition { Id = "s1-ref-exit-px-f", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-f" });
            session.Graph.Nodes.Add(reference);

            var decision = new SessionDecisionNodeDefinition { Id = "s1-decision", Prompt = "Go?" };
            decision.Outputs.Add(new GraphOutputDefinition { Id = "s1-decision-out", Kind = GraphPortKind.Normal });
            var option = new SessionDecisionOptionDefinition { Id = "s1-decision-opt-1", Label = "Yes", Sequence = new ActionSequenceDefinition { Id = "s1-decision-opt-1-seq" } };
            option.Sequence.Instances.Add(new SessionGotoInstanceDefinition { Id = "s1-goto-1", Label = "leave" });
            decision.Options.Add(option);
            decision.Outputs.Add(new GraphOutputDefinition { Id = "s1-goto-1-port", Kind = GraphPortKind.SessionGoto, SessionGotoActionInstanceId = "s1-goto-1", Label = "leave" });
            session.Graph.Nodes.Add(decision);

            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se1", SourceOutputId = "s1-start-out", TargetNodeId = "s1-ref" });
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se2", SourceOutputId = "s1-goto-1-port", TargetNodeId = "s1-start" });
            return session;
        }

        // ---------- clone semantics ----------

        [Test]
        public void ClonePhase_NewIdsEverywhere_GotoExitRemapped_EdgesSurvive()
        {
            var clone = ContentCloner.ClonePhase(BuildSharedPhase(), "p-clone");

            Assert.AreEqual("p-clone", clone.Phase.Id);
            Assert.AreNotEqual("px-c", clone.Phase.Exits[0].Id);
            Assert.AreNotEqual("n-entry", clone.Phase.Graph.Nodes[0].Id);
            Assert.AreNotEqual("goto-1", ((ActionNodeDefinition)clone.Phase.Graph.Nodes[1]).Sequence.Instances[0].Id);

            var gotoInstance = (PhaseGotoInstanceDefinition)((ActionNodeDefinition)clone.Phase.Graph.Nodes[1]).Sequence.Instances[0];
            Assert.AreEqual(clone.ExitIdMap["px-c"], gotoInstance.PhaseExitId, "goto exit remapped to clone exit");

            var edge = clone.Phase.Graph.Edges[0];
            Assert.AreEqual(clone.Phase.Graph.Nodes[0].Outputs[0].Id, edge.SourceOutputId, "edge source remapped");
            Assert.AreEqual(clone.Phase.Graph.Nodes[1].Id, edge.TargetNodeId, "edge target remapped");
        }

        [Test]
        public void CloneSession_NewIds_PhaseReferenceKeepsPhaseId_GotoPortRemapped()
        {
            var clone = ContentCloner.CloneSession(BuildSharedSession(), "s-copy", "Alpha (copy)");

            Assert.AreEqual("s-copy", clone.Session.Id);
            Assert.AreEqual("Alpha (copy)", clone.Session.Title);
            Assert.AreEqual("type-standard", clone.Session.SessionTypeId);

            var reference = (PhaseReferenceNodeDefinition)clone.Session.Graph.Nodes.Find(n => n.GetType() == typeof(PhaseReferenceNodeDefinition));
            Assert.AreEqual("p1", reference.PhaseId, "reuse stays reuse");

            var decision = (SessionDecisionNodeDefinition)clone.Session.Graph.Nodes.Find(n => n.GetType() == typeof(SessionDecisionNodeDefinition));
            var gotoInstance = (SessionGotoInstanceDefinition)decision.Options[0].Sequence.Instances[0];
            var port = decision.Outputs.Find(o => o.Kind == GraphPortKind.SessionGoto);
            Assert.AreEqual(gotoInstance.Id, port.SessionGotoActionInstanceId, "goto port remapped to cloned instance");
            Assert.AreEqual("leave", port.Label);
        }

        [Test]
        public void WriteClonedPhase_ThenSharedEditDoesNotAffectClone()
        {
            var shared = BuildSharedPhase();
            ReuseWriter.WriteClonedPhase(_connection, shared);
            var clone = ContentCloner.ClonePhase(shared, "p-clone");
            ReuseWriter.WriteClonedPhase(_connection, clone.Phase);

            // Edit the shared phase: rename its exit.
            PhaseExitRepository.Rename(_connection, "px-c", "Done");

            var content = GameContentSnapshotLoader.Load(_connection);
            var cloneExit = content.Phases.Find(p => p.Id == "p-clone").Exits.Find(x => x.Id == clone.ExitIdMap["px-c"]);
            Assert.AreEqual("Complete", cloneExit.Name, "clone unaffected by shared-phase edit");
        }

        // ---------- Make Unique ----------

        [Test]
        public void MakeUnique_DetachesPlacement_PortsAndEdgesSurvive()
        {
            var shared = BuildSharedPhase();
            ReuseWriter.WriteClonedPhase(_connection, shared);
            var session = BuildSharedSession();
            ReuseWriter.WriteClonedSession(_connection, session);
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "se-wired", SourceOutputId = "s1-ref-exit-px-c", TargetNodeId = "s1-decision",
            });

            var newPhaseId = MakeUniqueRepository.MakeUnique(_connection, shared, "s1-ref", "p-unique");

            // Placement re-pointed to the clone; port ids and edges preserved.
            Assert.AreEqual("p-unique", Scalar("SELECT phase_id FROM session_node_phase WHERE node_id='s1-ref'").ToString());
            Assert.AreEqual("s1-ref-exit-px-c", Scalar("SELECT id FROM session_node_output WHERE node_id='s1-ref' AND phase_exit_id IS NOT NULL ORDER BY ordinal LIMIT 1").ToString());
            Assert.AreEqual(1, Count("session_graph_edge WHERE id='se-wired'"));

            // Projected outputs now reference the clone's exit ids.
            var cloneExitId = ContentCloner.ClonePhase(shared, "p-unique").ExitIdMap["px-c"];
            Assert.AreEqual(cloneExitId, Scalar(
                "SELECT phase_exit_id FROM session_node_output WHERE id='s1-ref-exit-px-c'").ToString());

            // Session graph loads cleanly with the remapped placement.
            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, "s1");
            var reference = (PhaseReferenceNodeDefinition)graph.Nodes.Find(n => n.Id == "s1-ref");
            Assert.AreEqual("p-unique", reference.PhaseId);
            Assert.AreEqual(1, graph.Edges.FindAll(e => e.SourceOutputId == "s1-ref-exit-px-c").Count,
                "wired edge on the projected port survived Make Unique");
        }

        [Test]
        public void MakeUnique_ThenIsolatedExitEdit_DoesNotTouchSharedPhase()
        {
            var shared = BuildSharedPhase();
            ReuseWriter.WriteClonedPhase(_connection, shared);
            var session = BuildSharedSession();
            ReuseWriter.WriteClonedSession(_connection, session);

            var newPhaseId = MakeUniqueRepository.MakeUnique(_connection, shared, "s1-ref", "p-unique");

            // Delete an exit on the clone that no GOTO references — allowed with 1 placement.
            var cloneFailExit = ContentCloner.ClonePhase(shared, newPhaseId).ExitIdMap["px-f"];
            PhaseExitRepository.Delete(_connection, cloneFailExit);

            // Shared phase untouched (its Fail exit survives the clone edit).
            Assert.AreEqual(1, Count("phase_exit WHERE id='px-f'"), "shared exit survives clone edit");
            Assert.AreEqual(0, Count("phase_exit WHERE id='" + cloneFailExit + "'"));
        }

        // ---------- port lock data ----------

        [Test]
        public void Usage_FeedsPortLock_SecondPlacementBlocksExitEdit()
        {
            var shared = BuildSharedPhase();
            ReuseWriter.WriteClonedPhase(_connection, shared);
            var session = BuildSharedSession();
            ReuseWriter.WriteClonedSession(_connection, session);
            // A second placement in another session.
            SessionRepository.Create(_connection, "s2", "Beta", "type-standard");
            Execute("INSERT INTO session_graph_node VALUES ('ref2','s2','PhaseReference');");
            Execute("INSERT INTO session_node_phase VALUES ('ref2','p1');");

            var (sessions, placements) = PhaseRepository.Usage(_connection, "p1");
            Assert.AreEqual(2, sessions);
            Assert.AreEqual(2, placements);
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
