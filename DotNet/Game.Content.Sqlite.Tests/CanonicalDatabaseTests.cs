using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Guards the committed canonical Content/GameContent.db: it must always
    /// load as a complete snapshot and pass SQLite integrity checks. Locates
    /// the repo-relative DB (override via SQLITE_CANONICAL_DB env var).
    /// </summary>
    [TestFixture]
    public class CanonicalDatabaseTests
    {
        [Test]
        public void CanonicalDatabase_Loads_AndPassesIntegrityChecks()
        {
            var path = Environment.GetEnvironmentVariable("SQLITE_CANONICAL_DB");
            if (string.IsNullOrEmpty(path))
            {
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Content", "GameContent.db"));
            }
            Assert.That(File.Exists(path), Is.True, "Canonical DB missing at " + path);

            using (var connection = new SqliteConnection("Data Source=" + path))
            {
                ConnectionInitializer.Initialize(connection);

                var snapshot = GameContentSnapshotLoader.Load(connection);
                Assert.AreEqual(2, snapshot.Sessions.Count);
                Assert.AreEqual(6, snapshot.Phases.Count);
                Assert.AreEqual(7, snapshot.Cards.Count);
                Assert.AreEqual(5, snapshot.Actions.Count);
                Assert.AreEqual(1, snapshot.Resources.Count);
                Assert.AreEqual(7, snapshot.Deck.CardIds.Count);

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
    }
}
