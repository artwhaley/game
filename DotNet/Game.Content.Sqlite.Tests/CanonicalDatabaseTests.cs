using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Guards the committed canonical Content/GameContent.db: it must always pass
    /// SQLite integrity and foreign-key checks, be migrator-clean, and load
    /// through the v2 snapshot loader without losing structural content.
    /// </summary>
    [TestFixture]
    public class CanonicalDatabaseTests
    {
        [Test]
        public void CanonicalDatabase_PassesIntegrityChecks()
        {
            var path = CanonicalPath();
            Assert.That(File.Exists(path), Is.True, "Canonical DB missing at " + path);

            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA integrity_check;";
                    Assert.AreEqual("ok", command.ExecuteScalar());
                }
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA foreign_key_check;";
                    using (var reader = command.ExecuteReader())
                    {
                        Assert.IsFalse(reader.Read(), "foreign_key_check returned rows; canonical DB has FK violations");
                    }
                }
            }
        }

        [Test]
        public void CanonicalDatabase_IsMigrationV2()
        {
            using (var connection = new SqliteConnection("Data Source=" + CanonicalPath() + ";Mode=ReadOnly"))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT MAX(version) FROM core_schema_migration;";
                    Assert.AreEqual(2L, command.ExecuteScalar(), "canonical DB must be at core schema v2");
                }

                // v1 slot structures must be emptied by migration 2.
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM phase_slot;";
                    Assert.AreEqual(0L, command.ExecuteScalar(), "canonical DB must have no PhaseSlot rows");
                }
            }
        }

        [Test]
        public void CanonicalDatabase_LoadsThroughV2Loader()
        {
            using (var connection = new SqliteConnection("Data Source=" + CanonicalPath() + ";Mode=ReadOnly"))
            {
                connection.Open();
                var content = GameContentSnapshotLoader.Load(connection);

                Assert.Greater(content.Sessions.Count, 0, "sessions survived the migration");
                Assert.Greater(content.Phases.Count, 0, "phases survived the migration");
                Assert.Greater(content.Cards.Count, 0, "cards survived the migration");
                Assert.Greater(content.Deck.CardIds.Count, 0, "deck survived the migration");

                foreach (var session in content.Sessions)
                {
                    Assert.Greater(session.Graph.Nodes.Count, 0, $"session '{session.Id}' gained a graph");
                    Assert.Greater(session.Graph.Edges.Count, 0, $"session '{session.Id}' gained edges");
                }

                foreach (var phase in content.Phases)
                {
                    Assert.Greater(phase.Graph.Nodes.Count, 0, $"phase '{phase.Id}' gained a graph");
                    Assert.Greater(phase.Exits.Count, 0, $"phase '{phase.Id}' gained exits");
                }

                foreach (var card in content.Cards)
                {
                    Assert.IsNotNull(card.Sequence, $"card '{card.Id}' owns an action sequence");
                    Assert.Greater(card.Sequence.Instances.Count, 0, $"card '{card.Id}' has at least the default progress instance");
                }
            }
        }

        [Test]
        public void CanonicalDatabase_PlaysThroughTheSessionVm()
        {
            // End-to-end: the WPF reference host path — load the canonical DB,
            // run each session through the graph VM until it completes.
            using (var connection = new SqliteConnection("Data Source=" + CanonicalPath() + ";Mode=ReadOnly"))
            {
                connection.Open();
                var content = GameContentSnapshotLoader.Load(connection);

                foreach (var session in content.Sessions)
                {
                    var services = new Core.CoreServices(new NoOpDelay());
                    var engine = new Core.GameSessionEngine(content, session.Id, services);
                    var guard = 0;
                    while (!engine.IsComplete)
                    {
                        guard++;
                        Assert.Less(guard, 500, $"session '{session.Id}' did not complete (loop guard)");
                        var result = engine.AdvanceOneCardAsync(default).GetAwaiter().GetResult();
                        Assert.That(result.Kind, Is.AnyOf(
                            Core.AdvanceResultKind.CardCompleted,
                            Core.AdvanceResultKind.SessionCompleted), $"session '{session.Id}' advanced cleanly");
                    }
                }
            }
        }

        private static string CanonicalPath()
        {
            var path = Environment.GetEnvironmentVariable("SQLITE_CANONICAL_DB");
            if (string.IsNullOrEmpty(path))
            {
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Content", "GameContent.db"));
            }
            return path;
        }

        private sealed class NoOpDelay : Core.IGameDelay
        {
            public System.Threading.Tasks.Task DelayAsync(System.TimeSpan delay, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
