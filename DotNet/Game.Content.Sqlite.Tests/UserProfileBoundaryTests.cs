using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 20: the content database must stay distribution-safe — NO per-user
    /// data (kink preferences, equipment ownership, smart-toy configuration,
    /// persistent happiness). Those live in a FUTURE profile DB under
    /// UserProfilePaths (OS app-data), never inside GameContent.db. These tests
    /// guard the schema against accidentally growing profile tables here.
    /// </summary>
    [TestFixture]
    public class UserProfileBoundaryTests : IDisposable
    {
        private static readonly string[] ForbiddenTablePatterns =
        {
            "profile", "toy", "kink", "equipment", "preference",
            "device", "player_state", "temperature_state", "user", "save",
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
        public void FreshSchema_HasNoPerUserTablesOrState()
        {
            AssertNoPerUserTables(_connection, "fresh schema");
        }

        [Test]
        public void CanonicalDatabase_HasNoPerUserTables()
        {
            var path = CanonicalPath();
            Assert.That(File.Exists(path), Is.True, "Canonical DB missing at " + path);
            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();
                AssertNoPerUserTables(connection, "canonical Content/GameContent.db");
            }
        }

        private static void AssertNoPerUserTables(SqliteConnection connection, string what)
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

            foreach (var forbidden in ForbiddenTablePatterns)
            {
                foreach (var table in tables)
                {
                    Assert.That(table.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase), Is.LessThan(0),
                        $"{what} must not contain a per-user table matching '{forbidden}' (found '{table}').");
                }
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
