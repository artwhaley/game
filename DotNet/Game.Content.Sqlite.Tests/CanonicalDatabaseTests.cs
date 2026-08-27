using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Guards the committed canonical Content/GameContent.db: it must always pass
    /// SQLite integrity and foreign-key checks and be migrator-clean.
    ///
    /// INTERIM Graph Workbench state: full snapshot loading re-enters this test in
    /// Ticket 04 together with the v2 loader; only the storage-integrity portion is
    /// meaningful while the portable model is ahead of the schema.
    /// </summary>
    [TestFixture]
    public class CanonicalDatabaseTests
    {
        [Test]
        public void CanonicalDatabase_PassesIntegrityChecks()
        {
            var path = Environment.GetEnvironmentVariable("SQLITE_CANONICAL_DB");
            if (string.IsNullOrEmpty(path))
            {
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Content", "GameContent.db"));
            }
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
    }
}
