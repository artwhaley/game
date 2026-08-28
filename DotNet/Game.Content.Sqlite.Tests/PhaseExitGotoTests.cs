using System;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 15: phase exits + inline PhaseGoto + live session-port projection.
    /// Identity is the exit id: renaming must never break wiring, adding an exit
    /// must project onto every placement immediately, and PhaseGoto instances
    /// persist their exact exit mapping for runtime.
    /// </summary>
    [TestFixture]
    public class PhaseExitGotoTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        private Func<DbConnection> Conn => () =>
        {
            var connection = new SqliteConnection("Data Source=" + _dbPath);
            connection.Open();
            ConnectionInitializer.Initialize(connection);
            return connection;
        };

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-goto-{Guid.NewGuid():N}.db");
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

        // ---------- exit rename + live projection ----------

        [Test]
        public void RenameExit_UpdatesName_KeepsId_AndProjectedSocketWiring()
        {
            SetupPhaseWithPlacement();

            // Wire the projected socket of exit px-complete.
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "se1", SourceOutputId = "ref-exit-px-complete", TargetNodeId = "end",
            });

            PhaseExitRepository.Rename(_connection, "px-complete", "Completed!");

            // Exit row renamed, id untouched.
            var content = GameContentSnapshotLoader.Load(_connection);
            var exit = content.Phases.Find(p => p.Id == "p1").Exits.Find(x => x.Id == "px-complete");
            Assert.AreEqual("Completed!", exit.Name);
            Assert.AreEqual("px-complete", exit.Id);

            // Projected socket survived with its edge (identity is the exit id).
            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, "s1");
            Assert.AreEqual(1, graph.Edges.Count, "wiring survives rename");
            Assert.AreEqual("se1", graph.Edges[0].Id);
            Assert.AreEqual("ref-exit-px-complete", graph.Edges[0].SourceOutputId);
        }

        [Test]
        public void AddExit_ProjectsOntoEveryPlacement()
        {
            SetupPhaseWithPlacement();

            PhaseExitRepository.Create(_connection, "p1", new PhaseExitDefinition { Id = "px-third", Name = "Third" }, 2);
            PhaseExitRepository.SyncProjectedSockets(_connection, "p1", "px-third", 2);

            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, "s1");
            var reference = (PhaseReferenceNodeDefinition)graph.Nodes.Find(n => n.Id == "ref");
            var projected = reference.Outputs.Find(o => o.PhaseExitId == "px-third");
            Assert.IsNotNull(projected, "new exit projected onto the placement");
            Assert.AreEqual("ref-exit-px-third", projected.Id);
            Assert.AreEqual(GraphPortKind.PhaseExit, projected.Kind);
        }

        [Test]
        public void SyncProjectedSockets_IsIdempotent()
        {
            SetupPhaseWithPlacement();
            PhaseExitRepository.SyncProjectedSockets(_connection, "p1", "px-complete", 0);
            PhaseExitRepository.SyncProjectedSockets(_connection, "p1", "px-complete", 0);

            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, "s1");
            var reference = (PhaseReferenceNodeDefinition)graph.Nodes.Find(n => n.Id == "ref");
            Assert.AreEqual(1, reference.Outputs.FindAll(o => o.PhaseExitId == "px-complete").Count);
        }

        [Test]
        public void DeleteExit_CascadesProjectedSocketAndItsEdge_Transactionally()
        {
            SetupPhaseWithPlacement();
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "se1", SourceOutputId = "ref-exit-px-complete", TargetNodeId = "end",
            });

            PhaseExitRepository.Delete(_connection, "px-complete");

            Assert.AreEqual(0, Count("session_node_output WHERE phase_exit_id='px-complete'"));
            Assert.AreEqual(0, Count("session_graph_edge"));
            // Other exit still projected.
            var graph = GameContentSnapshotLoader.LoadSessionGraph(_connection, "s1");
            var reference = (PhaseReferenceNodeDefinition)graph.Nodes.Find(n => n.Id == "ref");
            Assert.IsNotNull(reference.Outputs.Find(o => o.PhaseExitId == "px-fail"));
        }

        // ---------- PhaseGoto instances ----------

        [Test]
        public void AddPhaseGoto_RoundTripsWithExactExitId()
        {
            SetupPhaseWithPlacement();
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            action.Outputs.Add(new GraphOutputDefinition { Id = "n-action-out", Kind = GraphPortKind.Normal });
            PhaseGraphRepository.AddNode(_connection, "p1", action);

            PhaseGraphRepository.AddPhaseGoto(_connection, "n-action", "px-complete");

            var content = GameContentSnapshotLoader.Load(_connection);
            var loaded = (ActionNodeDefinition)content.Phases.Find(p => p.Id == "p1")
                .Graph.Nodes.Find(n => n.Id == "n-action");
            Assert.AreEqual(1, loaded.Sequence.Instances.Count);
            var gotoInstance = (PhaseGotoInstanceDefinition)loaded.Sequence.Instances[0];
            Assert.AreEqual("px-complete", gotoInstance.PhaseExitId);
            Assert.IsTrue(gotoInstance.IsBlocking);
        }

        [Test]
        public void SetPhaseGotoExit_RepointsInstance_AndListReflectsIt()
        {
            SetupPhaseWithPlacement();
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            PhaseGraphRepository.AddNode(_connection, "p1", action);
            PhaseGraphRepository.AddPhaseGoto(_connection, "n-action", "px-complete");

            var before = PhaseGraphRepository.ListPhaseGotoInstances(_connection, "n-action");
            Assert.AreEqual(1, before.Count);
            PhaseGraphRepository.SetPhaseGotoExit(_connection, before[0].InstanceId, "px-fail");

            var after = PhaseGraphRepository.ListPhaseGotoInstances(_connection, "n-action");
            Assert.AreEqual("px-fail", after[0].ExitId);
        }

        [Test]
        public void PhaseGoto_UnassignedIsNull_AndCanBeAssignedLater()
        {
            SetupPhaseWithPlacement();
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            PhaseGraphRepository.AddNode(_connection, "p1", action);

            PhaseGraphRepository.AddPhaseGoto(_connection, "n-action", null);
            var list = PhaseGraphRepository.ListPhaseGotoInstances(_connection, "n-action");
            Assert.IsNull(list[0].ExitId);
            var loadedAction = (ActionNodeDefinition)GameContentSnapshotLoader.Load(_connection)
                .Phases.Find(p => p.Id == "p1").Graph.Nodes.Find(n => n.Id == "n-action");
            Assert.IsNull(((PhaseGotoInstanceDefinition)loadedAction.Sequence.Instances[0]).PhaseExitId);

            PhaseGraphRepository.SetPhaseGotoExit(_connection, list[0].InstanceId, "px-fail");
            Assert.AreEqual("px-fail", PhaseGraphRepository.ListPhaseGotoInstances(_connection, "n-action")[0].ExitId);
            PhaseGraphRepository.SetPhaseGotoExit(_connection, list[0].InstanceId, null);
            Assert.IsNull(PhaseGraphRepository.ListPhaseGotoInstances(_connection, "n-action")[0].ExitId);
        }

        [Test]
        public void PhaseGoto_NonexistentExitIsRejectedWithoutLeavingOrphanInstance()
        {
            SetupPhaseWithPlacement();
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            PhaseGraphRepository.AddNode(_connection, "p1", action);

            Assert.Throws<SqliteException>(() => PhaseGraphRepository.AddPhaseGoto(_connection, "n-action", "missing-exit"));
            Assert.AreEqual(0, Count("action_instance"));
        }

        [Test]
        public void RemovePhaseGoto_DeletesInstanceAndSubtypeRow()
        {
            SetupPhaseWithPlacement();
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            PhaseGraphRepository.AddNode(_connection, "p1", action);
            PhaseGraphRepository.AddPhaseGoto(_connection, "n-action", "px-complete");
            var list = PhaseGraphRepository.ListPhaseGotoInstances(_connection, "n-action");

            PhaseGraphRepository.RemovePhaseGoto(_connection, list[0].InstanceId);

            Assert.AreEqual(0, Count("action_instance_phase_goto"));
            Assert.AreEqual(0, PhaseGraphRepository.ListPhaseGotoInstances(_connection, "n-action").Count);
        }

        [Test]
        public void DeleteExit_ReferencedByGoto_IsBlocked_ByRestrict()
        {
            SetupPhaseWithPlacement();
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            PhaseGraphRepository.AddNode(_connection, "p1", action);
            PhaseGraphRepository.AddPhaseGoto(_connection, "n-action", "px-complete");

            Assert.Throws<SqliteException>(() => PhaseExitRepository.Delete(_connection, "px-complete"));
            // The exit survives — we never silently auto-delete an exit.
            Assert.AreEqual(1, Count("phase_exit WHERE id='px-complete'"));
        }

        [Test]
        public void ForceDeleteExit_ClearsReferences_AndUndoRestoresExactAssignmentAndEdge()
        {
            SetupPhaseWithPlacement();
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition { Id = "n-action-seq" },
            };
            PhaseGraphRepository.AddNode(_connection, "p1", action);
            PhaseGraphRepository.AddPhaseGoto(_connection, "n-action", "px-complete");
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition
            {
                Id = "se-exact", SourceOutputId = "ref-exit-px-complete", TargetNodeId = "end",
            });

            var projected = AuthoringUndo.ExitProjectedEdges(_connection, "px-complete");
            var stack = new AuthoringCommandStack();
            stack.PushOrMerge(new DeleteExitCommand(Conn, "p1",
                new PhaseExitDefinition { Id = "px-complete", Name = "Complete" }, 0, projected));

            Assert.AreEqual(0, Count("phase_exit WHERE id='px-complete'"));
            Assert.IsNull(AuthoringUndo.GetPhaseGotoExit(_connection, "n-action-goto-0"));
            Assert.AreEqual(0, Count("session_graph_edge WHERE id='se-exact'"));

            stack.Undo();
            Assert.AreEqual("px-complete", AuthoringUndo.GetPhaseGotoExit(_connection, "n-action-goto-0"));
            Assert.AreEqual(1, Count("session_graph_edge WHERE id='se-exact' AND session_id='s1'"));
        }

        // ---------- helpers ----------

        private void SetupPhaseWithPlacement()
        {
            PhaseRepository.Create(_connection, "p1", "Cold Start");
            Execute("INSERT INTO phase_exit VALUES ('px-complete','p1',0,'Complete');");
            Execute("INSERT INTO phase_exit VALUES ('px-fail','p1',1,'Fail');");

            SessionRepository.Create(_connection, "s1", "Alpha", "type-standard");
            Execute("INSERT INTO session_graph_node VALUES ('ref','s1','PhaseReference');");
            Execute("INSERT INTO session_node_phase VALUES ('ref','p1');");
            Execute("INSERT INTO session_node_output VALUES ('ref-exit-px-complete','ref','phase_exit',0,NULL,'px-complete',NULL);");
            Execute("INSERT INTO session_node_output VALUES ('ref-exit-px-fail','ref','phase_exit',1,NULL,'px-fail',NULL);");
            Execute("INSERT INTO session_graph_node VALUES ('end','s1','End');");
        }

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
