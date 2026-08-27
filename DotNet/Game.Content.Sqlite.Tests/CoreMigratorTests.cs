using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    [TestFixture]
    public class CoreMigratorTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "sqlite-tests-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            // Microsoft.Data.Sqlite pools connections; clear the pool so the
            // temp DB file handle is released before deletion.
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private SqliteConnection OpenNewConnection()
        {
            var connection = new SqliteConnection("Data Source=" + _dbPath);
            ConnectionInitializer.Initialize(connection);
            return connection;
        }

        [Test]
        public void EnsureSchema_AppliesV1_AndCoreTablesExist()
        {
            ConnectionInitializer.Initialize(_connection);
            var version = CoreMigrator.EnsureSchema(_connection);

            Assert.AreEqual(2, version, "both migrations apply on a fresh database");
            Assert.IsTrue(TableExists(_connection, "session"));
            Assert.IsTrue(TableExists(_connection, "phase_slot"));
            Assert.IsTrue(TableExists(_connection, "phase_slot_candidate"));
            Assert.IsTrue(TableExists(_connection, "action_choice"));
            Assert.IsTrue(TableExists(_connection, "card_action"));
            Assert.IsTrue(TableExists(_connection, "resource"));
            // v2 additions:
            Assert.IsTrue(TableExists(_connection, "action_instance"));
            Assert.IsTrue(TableExists(_connection, "phase_graph_edge"));
            Assert.IsTrue(TableExists(_connection, "session_graph_node"));
        }

        [Test]
        public void EnsureSchema_IsIdempotent_OnReopen()
        {
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);
            CoreMigrator.EnsureSchema(_connection);

            _connection.Dispose();
            using (var reopened = OpenNewConnection())
            {
                CoreMigrator.EnsureSchema(reopened);
                Assert.AreEqual(2, MigrationRowCount(reopened), "one ledger row per migration");
            }
        }

        [Test]
        public void ConnectionInitializer_EnablesForeignKeys()
        {
            ConnectionInitializer.Initialize(_connection);

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys;";
                var value = command.ExecuteScalar();
                Assert.AreEqual(1L, Convert.ToInt64(value));
            }
        }

        [Test]
        public void EnsureSchema_LeavesUnknownHostTableUntouched()
        {
            ConnectionInitializer.Initialize(_connection);
            Execute(_connection, "CREATE TABLE unity_fake_extension (id TEXT PRIMARY KEY, payload TEXT NOT NULL);");

            CoreMigrator.EnsureSchema(_connection);

            Assert.IsTrue(TableExists(_connection, "unity_fake_extension"));
            Execute(_connection, "INSERT INTO unity_fake_extension (id, payload) VALUES ('x', 'kept');");
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT payload FROM unity_fake_extension WHERE id = 'x';";
                Assert.AreEqual("kept", command.ExecuteScalar());
            }
        }

        [Test]
        public void MigrationTable_RecordsEveryMigrationInOrder()
        {
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT version, name FROM core_schema_migration ORDER BY version;";
                using (var reader = command.ExecuteReader())
                {
                    Assert.IsTrue(reader.Read());
                    Assert.AreEqual(1, reader.GetInt32(0));
                    Assert.AreEqual("core-schema-v1", reader.GetString(1));
                    Assert.IsTrue(reader.Read());
                    Assert.AreEqual(2, reader.GetInt32(0));
                    Assert.AreEqual("core-graph-schema-v2", reader.GetString(1));
                    Assert.IsFalse(reader.Read());
                }
            }
        }

        private bool TableExists(SqliteConnection connection, string name)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
                command.Parameters.AddWithValue("@name", name);
                return Convert.ToInt64(command.ExecuteScalar()) == 1L;
            }
        }

        private long MigrationRowCount(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM core_schema_migration;";
                return Convert.ToInt64(command.ExecuteScalar());
            }
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
