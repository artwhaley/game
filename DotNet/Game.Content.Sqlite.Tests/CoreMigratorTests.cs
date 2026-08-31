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
        public void EnsureSchema_AppliesAllMigrations_AndCoreTablesExist()
        {
            ConnectionInitializer.Initialize(_connection);
            var version = CoreMigrator.EnsureSchema(_connection);

            Assert.AreEqual(CoreMigrations.MaxVersion, version, "all core migrations apply on a fresh database");
            Assert.IsTrue(TableExists(_connection, "session"));
            Assert.IsTrue(TableExists(_connection, "card"));
            Assert.IsTrue(TableExists(_connection, "resource"));
            // v2 additions:
            Assert.IsTrue(TableExists(_connection, "action_instance"));
            Assert.IsTrue(TableExists(_connection, "phase_graph_edge"));
            Assert.IsTrue(TableExists(_connection, "session_graph_node"));
            // v3 WPF authoring additions:
            Assert.IsTrue(TableExists(_connection, "wpf_session_node_layout"));
            Assert.IsTrue(TableExists(_connection, "wpf_phase_node_layout"));
            Assert.IsTrue(TableExists(_connection, "wpf_viewport_state"));
            // v5 Milestone B additions:
            Assert.IsTrue(TableExists(_connection, "card_tag_definition"));
            Assert.IsTrue(TableExists(_connection, "kink_definition"));
            Assert.IsTrue(TableExists(_connection, "equipment_definition"));
            Assert.IsTrue(TableExists(_connection, "smart_toy_capability_definition"));
            Assert.IsTrue(TableExists(_connection, "card_kink"));
            Assert.IsTrue(TableExists(_connection, "card_required_equipment"));
            Assert.IsTrue(TableExists(_connection, "card_required_smart_toy_capability"));
            Assert.IsTrue(TableExists(_connection, "phase_card_all_tag"));
            Assert.IsTrue(TableExists(_connection, "phase_card_any_tag"));
            Assert.IsTrue(TableExists(_connection, "session_card_weighting"));
            Assert.IsTrue(TableExists(_connection, "session_type_required_smart_toy_capability"));
            // v10 editor-only Action Block templates:
            Assert.IsTrue(TableExists(_connection, "wpf_action_block"));
            // v11 WPF-only graph portal metadata:
            Assert.IsTrue(TableExists(_connection, "wpf_session_edge_portal_pair"));
            Assert.IsTrue(TableExists(_connection, "wpf_phase_edge_portal_pair"));
            // v5 drops the superseded v1 structures:
            Assert.IsFalse(TableExists(_connection, "phase_slot"));
            Assert.IsFalse(TableExists(_connection, "card_deck"));
            Assert.IsFalse(TableExists(_connection, "action"));
            Assert.IsFalse(TableExists(_connection, "tag"));
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
                Assert.AreEqual(CoreMigrations.MaxVersion, MigrationRowCount(reopened), "one ledger row per migration");
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

            var applied = CoreMigrations.All;
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT version, name FROM core_schema_migration ORDER BY version;";
                using (var reader = command.ExecuteReader())
                {
                    for (var i = 0; i < applied.Count; i++)
                    {
                        Assert.IsTrue(reader.Read(), "missing ledger row for migration " + applied[i].Version);
                        Assert.AreEqual(applied[i].Version, reader.GetInt32(0));
                        Assert.AreEqual(applied[i].Name, reader.GetString(1));
                    }
                    Assert.IsFalse(reader.Read(), "more ledger rows than registered migrations");
                }
            }
        }

        [Test]
        public void SplitStatements_KeepsSQLiteTriggerBodiesTogether()
        {
            var statements = CoreMigrator.SplitStatements(
                "CREATE TABLE sample (id TEXT);" +
                "CREATE TRIGGER trigger_sample BEFORE INSERT ON sample BEGIN " +
                "SELECT CASE WHEN NEW.id = '' THEN RAISE(ABORT, 'empty') END; " +
                "END;" +
                "CREATE INDEX index_sample ON sample(id);");

            Assert.That(statements, Has.Count.EqualTo(3));
            Assert.That(statements[1], Does.Contain("CREATE TRIGGER"));
            Assert.That(statements[1], Does.Contain("END"));
            Assert.That(statements[2], Does.Contain("CREATE INDEX"));
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
