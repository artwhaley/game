using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 20 boundary, updated for Milestone B: the content database must
    /// stay distribution-safe — NO per-user STATE (kink preferences rows,
    /// equipment ownership rows, capability availability rows, persistent
    /// happiness). User state lives in the separate UserProfile.db under
    /// UserProfilePaths (OS app-data), never inside GameContent.db.
    ///
    /// Milestone B legitimately adds AUTHORED CATALOG tables here
    /// (kink_definition, equipment_definition, smart_toy_capability_definition,
    /// card_tag_definition): those are authored content a Session/Card may
    /// reference — the catalog says a kink exists; only the profile says the
    /// user's preference for it. The forbidden list therefore matches state
    /// table names, not catalog names.
    /// </summary>
    [TestFixture]
    public class UserProfileBoundaryTests : IDisposable
    {
        /// <summary>Per-user STATE table names (never valid in GameContent.db).</summary>
        private static readonly string[] ForbiddenTableNames =
        {
            "profile",
            "user_profile",
            "kink_preference",
            "equipment_owned",
            "smart_toy_capability_available",
            "player_state",
            "temperature_state",
            "save_data",
        };

        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-boundary-{Guid.NewGuid():N}.db");
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
        public void FreshSchema_HasNoPerUserStateTables()
        {
            AssertNoPerUserState(_connection, "fresh schema");
        }

        [Test]
        public void CanonicalDatabase_HasNoPerUserState()
        {
            var path = CanonicalPath();
            Assert.That(File.Exists(path), Is.True, "Canonical DB missing at " + path);
            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();
                AssertNoPerUserState(connection, "canonical Content/GameContent.db");
            }
        }

        private static void AssertNoPerUserState(SqliteConnection connection, string what)
        {
            var tables = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) tables.Add(reader.GetString(0));
                }
            }

            foreach (var forbidden in ForbiddenTableNames)
            {
                Assert.That(tables, Does.Not.Contain(forbidden),
                    $"{what} must not contain per-user state table '{forbidden}'.");
            }

            // No table stores a mutable happiness value (temperature_definition
            // is CONTENT; the runtime TemperatureState is memory-only).
            Assert.That(tables, Does.Not.Contain("temperature_state"), what + " has no persistent temperature state");
            Assert.That(tables, Does.Not.Contain("player_state"), what + " has no persistent player state");
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
    }
}
