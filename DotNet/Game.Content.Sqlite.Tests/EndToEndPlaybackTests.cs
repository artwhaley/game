using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 21 FINAL end-to-end gate: author a session + phase through the
    /// authoring repositories, reload the portable snapshot, and play it to
    /// completion through the REAL Core GameSessionEngine — the exact pipeline
    /// the Workbench preview runs (DB -> snapshot -> Core graph VM).
    /// </summary>
    [TestFixture]
    public class EndToEndPlaybackTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-e2e-{Guid.NewGuid():N}.db");
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

        [Test]
        public void AuthoredPhase_SurvivesReload_AndPlaysToCompletion()
        {
            // --- author: one phase with Entry -> Exec -> progress-check -> goto ---
            var phase = new PhaseDefinition { Id = "p-main", Title = "Main" };
            phase.Exits.Add(new PhaseExitDefinition { Id = "px-complete", Name = "Complete" });
            PhaseRepository.Create(_connection, phase.Id, phase.Title);
            PhaseExitRepository.Create(_connection, phase.Id, phase.Exits[0], 0);

            var entry = new PhaseEntryNodeDefinition { Id = "n-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = "n-entry-out", Kind = GraphPortKind.Normal });
            var exec = new CardExecutorNodeDefinition { Id = "n-exec" };
            exec.Outputs.Add(new GraphOutputDefinition { Id = "n-exec-out", Kind = GraphPortKind.Normal });
            var check = new VariableCheckNodeDefinition
            {
                Id = "n-check",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 50f,
            };
            check.Outputs.Add(new GraphOutputDefinition { Id = "n-check-true", Kind = GraphPortKind.True });
            check.Outputs.Add(new GraphOutputDefinition { Id = "n-check-false", Kind = GraphPortKind.False });
            var action = new ActionNodeDefinition { Id = "n-goto", Sequence = new ActionSequenceDefinition { Id = "n-goto-seq" } };
            action.Sequence.Instances.Add(new PhaseGotoInstanceDefinition { Id = "n-goto-inst", PhaseExitId = "px-complete" });
            action.Outputs.Add(new GraphOutputDefinition { Id = "n-goto-out", Kind = GraphPortKind.Normal });

            PhaseGraphRepository.AddNode(_connection, phase.Id, entry);
            PhaseGraphRepository.AddNode(_connection, phase.Id, exec);
            PhaseGraphRepository.AddNode(_connection, phase.Id, check);
            PhaseGraphRepository.AddNode(_connection, phase.Id, action);
            PhaseGraphRepository.AddEdge(_connection, phase.Id, new GraphEdgeDefinition { Id = "e1", SourceOutputId = "n-entry-out", TargetNodeId = "n-exec" });
            PhaseGraphRepository.AddEdge(_connection, phase.Id, new GraphEdgeDefinition { Id = "e2", SourceOutputId = "n-exec-out", TargetNodeId = "n-check" });
            PhaseGraphRepository.AddEdge(_connection, phase.Id, new GraphEdgeDefinition { Id = "e3", SourceOutputId = "n-check-false", TargetNodeId = "n-exec" });
            PhaseGraphRepository.AddEdge(_connection, phase.Id, new GraphEdgeDefinition { Id = "e4", SourceOutputId = "n-check-true", TargetNodeId = "n-goto" });

            // --- author: one session Start -> ref -> End ---
            SessionRepository.Create(_connection, "s1", "E2E Session", "type-standard");
            var start = new SessionStartNodeDefinition { Id = "sn-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "sn-start-out", Kind = GraphPortKind.Normal });
            var reference = new PhaseReferenceNodeDefinition { Id = "sn-ref", PhaseId = "p-main" };
            reference.Outputs.Add(new GraphOutputDefinition { Id = "sn-ref-out", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-complete" });
            var end = new SessionEndNodeDefinition { Id = "sn-end" };
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionGraphRepository.AddNode(_connection, "s1", reference);
            SessionGraphRepository.AddNode(_connection, "s1", end);
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition { Id = "se1", SourceOutputId = "sn-start-out", TargetNodeId = "sn-ref" });
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition { Id = "se2", SourceOutputId = "sn-ref-out", TargetNodeId = "sn-end" });

            // --- a card that yields +10 progress per draw ---
            var card = new CardDefinition { Id = "c1", Title = "Push" };
            card.Sequence = new ActionSequenceDefinition { Id = "c1-seq" };
            card.Sequence.Instances.Add(new IncrementProgressInstanceDefinition { Id = "c1-inc", Amount = 10f });
            Sql.Execute(_connection, null,
                "INSERT INTO card_deck (id, title) VALUES ('deck', 'Deck');");
            Sql.Execute(_connection, null,
                "INSERT INTO card (id, title, action_sequence_id) VALUES (@id, @title, @seq);",
                ("id", card.Id), ("title", card.Title), ("seq", "c1-seq"));
            Sql.Execute(_connection, null,
                "INSERT INTO action_sequence (id) VALUES (@id);", ("id", "c1-seq"));
            Sql.Execute(_connection, null,
                "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                "VALUES (@id, @seq, 0, @type, 0);",
                ("id", "c1-inc"), ("seq", "c1-seq"), ("type", ActionType.IncrementProgressV2));
            Sql.Execute(_connection, null,
                "INSERT INTO action_instance_increment_progress (action_instance_id, amount) VALUES (@i, 10);",
                ("i", "c1-inc"));
            Sql.Execute(_connection, null,
                "INSERT INTO card_deck_card (deck_id, ordinal, card_id) VALUES ('deck', 0, @id);", ("id", card.Id));

            // --- DB -> snapshot -> real Core engine, to completion ---
            var content = GameContentSnapshotLoader.Load(_connection);
            Assert.AreEqual(1, content.Sessions.Count);
            Assert.AreEqual(1, content.Phases.Count);

            var engine = new TruthCardGame.Core.GameSessionEngine(content, "s1",
                new TruthCardGame.Core.CoreServices(new NoOpDelay()));
            var cardsDrawn = 0;
            engine.CardStarted += _ => cardsDrawn++;
            var guard = 0;
            var waitingForContinue = false;
            while (!engine.IsComplete)
            {
                guard++;
                Assert.Less(guard, 200, "end-to-end run did not complete (loop guard)");
                var result = (waitingForContinue
                        ? engine.ContinueAsync(default)
                        : engine.RunUntilYieldAsync(default))
                    .GetAwaiter().GetResult();
                waitingForContinue = result.Kind == TruthCardGame.Core.AdvanceResultKind.WaitForContinue;
                Assert.That(result.Kind, Is.AnyOf(
                    TruthCardGame.Core.AdvanceResultKind.WaitForContinue,
                    TruthCardGame.Core.AdvanceResultKind.SessionCompleted));
            }

            // 5 draws (5 x +10 progress = 50 >= check target), then the GOTO
            // exits the phase and the session ends.
            Assert.AreEqual(5, cardsDrawn, "5 draws reach the progress-check target");
            Assert.IsTrue(engine.IsComplete, "authored session plays to completion");
        }

        private sealed class NoOpDelay : TruthCardGame.Core.IGameDelay
        {
            public System.Threading.Tasks.Task DelayAsync(TimeSpan delay, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
