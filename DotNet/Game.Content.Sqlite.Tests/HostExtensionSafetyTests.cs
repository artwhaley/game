using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 11 boundary, updated for v5: prove the core DB is safe for host
    /// extensions (Unity/WPF). A test-only extension table — the exact shape a
    /// Unity binding layer would own — must survive every ordinary authoring
    /// operation, cascade only on intentional deletes, never be rebuilt by
    /// core migrations, and be invisible to snapshot loading. Host tables now
    /// bind to action_instance (v2+ ownership); the v1 top-level action table
    /// they originally bound to was dropped by migration 5.
    /// </summary>
    [TestFixture]
    public class HostExtensionSafetyTests
    {
        private const string CreateHostTable =
            "CREATE TABLE IF NOT EXISTS unity_fake_action_binding (" +
            "  action_instance_id TEXT PRIMARY KEY, " +
            "  payload TEXT NOT NULL, " +
            "  FOREIGN KEY(action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE" +
            ");";

        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "sqlite-hostext-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);
            Execute(CreateHostTable);
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private static string Id() => "id-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        private void InsertInstance(string id, string sequenceId, int ordinal, string type)
        {
            Execute("INSERT INTO action_sequence (id) VALUES ('" + sequenceId + "');");
            Execute("INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                    "VALUES ('" + id + "', '" + sequenceId + "', " + ordinal + ", '" + type + "', 0);");
        }

        // --- 1. In-place Action Instance update preserves binding -------------

        [Test]
        public void UpdateInstanceInPlace_PreservesHostBinding()
        {
            var instanceId = Id();
            InsertInstance(instanceId, "seq-" + Id(), 0, ActionType.Debug);

            Execute("INSERT INTO unity_fake_action_binding (action_instance_id, payload) VALUES ('" + instanceId + "', 'payload-a');");

            Execute("UPDATE action_instance SET is_blocking = 1 WHERE id = '" + instanceId + "';");

            Assert.AreEqual("payload-a", BindingPayload(instanceId));
        }

        // --- 2. Unrelated Session/Phase edits preserve bindings ---------------

        [Test]
        public void UnrelatedSessionAndPhaseEdits_PreserveHostBinding()
        {
            var instanceId = Id();
            InsertInstance(instanceId, "seq-" + Id(), 0, ActionType.Debug);
            Execute("INSERT INTO unity_fake_action_binding (action_instance_id, payload) VALUES ('" + instanceId + "', 'payload-a');");

            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "S", "type-standard");
            SessionRepository.Rename(_connection, sessionId, "S2");
            SessionRepository.ReplaceCardWeighting(_connection, sessionId, new SessionCardWeightingDefinition { LikeBase = 2f });

            var phaseId = Id();
            PhaseRepository.Create(_connection, phaseId, "P");
            PhaseRepository.Rename(_connection, phaseId, "P2");

            Assert.AreEqual("payload-a", BindingPayload(instanceId));
        }

        // --- 3. Sequence reorder preserves instance rows/bindings ------------

        [Test]
        public void SequenceInstanceReorder_PreservesInstanceRowsAndBindings()
        {
            var i1 = Id();
            var i2 = Id();
            var sequenceId = "seq-" + Id();
            InsertInstance(i1, sequenceId, 0, ActionType.Debug);
            InsertInstance(i2, sequenceId, 1, ActionType.Debug);
            Execute("INSERT INTO unity_fake_action_binding (action_instance_id, payload) VALUES ('" + i1 + "', 'b1');");
            Execute("INSERT INTO unity_fake_action_binding (action_instance_id, payload) VALUES ('" + i2 + "', 'b2');");

            // Swap ordinals safely via the large-offset reorder convention.
            using (var transaction = _connection.BeginTransaction())
            {
                Execute("UPDATE action_instance SET ordinal = 90 WHERE id = '" + i1 + "';");
                Execute("UPDATE action_instance SET ordinal = 91 WHERE id = '" + i2 + "';");
                Execute("UPDATE action_instance SET ordinal = 0 WHERE id = '" + i2 + "';");
                Execute("UPDATE action_instance SET ordinal = 1 WHERE id = '" + i1 + "';");
                transaction.Commit();
            }

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT id FROM action_instance WHERE action_sequence_id = '" + sequenceId + "' ORDER BY ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    Assert.IsTrue(reader.Read());
                    Assert.AreEqual(i2, reader.GetString(0));
                    Assert.IsTrue(reader.Read());
                    Assert.AreEqual(i1, reader.GetString(0));
                    Assert.IsFalse(reader.Read());
                }
            }
            Assert.AreEqual("b1", BindingPayload(i1));
            Assert.AreEqual("b2", BindingPayload(i2));
            AssertRowCount("action_instance", 2);
        }

        // --- 4. Card relation reorder preserves definition rows --------------

        [Test]
        public void CardTagReorder_PreservesDefinitionRows()
        {
            var tagA = Id();
            var tagB = Id();
            CatalogRepositories.CreateCardTag(_connection, new CardTagDefinition { Id = tagA, Title = "A" });
            CatalogRepositories.CreateCardTag(_connection, new CardTagDefinition { Id = tagB, Title = "B" });
            var cardId = Id();
            CardRepository.Create(_connection, new CardDefinition { Id = cardId, Title = "C", CardTagIds = { tagA, tagB } });

            AssertRowCount("card_tag", 2);
            AssertRowCount("card_tag_definition", 2);

            // Removing one relation leaves the definition rows alone.
            Execute("DELETE FROM card_tag WHERE card_id = '" + cardId + "' AND tag_id = '" + tagA + "';");
            AssertRowCount("card_tag_definition", 2);
            AssertRowCount("card_tag", 1);
        }

        // --- 5. Intentional instance delete cascades owned host binding -------

        [Test]
        public void IntentionalInstanceDelete_CascadesOwnedHostBinding()
        {
            var instanceId = Id();
            InsertInstance(instanceId, "seq-" + Id(), 0, ActionType.Debug);
            Execute("INSERT INTO unity_fake_action_binding (action_instance_id, payload) VALUES ('" + instanceId + "', 'payload-a');");

            // Scoped delete — the only delete pattern the codebase uses.
            using (var transaction = _connection.BeginTransaction())
            {
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM action_instance WHERE id = @id;";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@id";
                    parameter.Value = instanceId;
                    command.Parameters.Add(parameter);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            AssertRowCount("unity_fake_action_binding", 0);
        }

        // --- 6. Core migrations preserve unknown host tables ------------------

        [Test]
        public void CoreMigrations_PreserveUnknownHostTables()
        {
            // Simulate the real extension-first upgrade path: core applied,
            // host extension installs its binding table, then an older core
            // (empty migration ledger) re-applies the migration on top. The
            // host table and its rows must survive untouched.
            Execute("DELETE FROM core_schema_migration;");
            var instanceId = Id();
            InsertInstance(instanceId, "seq-" + Id(), 0, ActionType.Debug);
            Execute("INSERT INTO unity_fake_action_binding (action_instance_id, payload) VALUES ('" + instanceId + "', 'kept');");

            CoreMigrator.EnsureSchema(_connection);

            AssertRowCount("unity_fake_action_binding", 1);
            Assert.AreEqual("kept", BindingPayload(instanceId));

            // Idempotent re-run also leaves the host table and its rows alone.
            CoreMigrator.EnsureSchema(_connection);
            Assert.AreEqual("kept", BindingPayload(instanceId));
        }

        // --- 7. Snapshot loading ignores host tables --------------------------

        [Test]
        public void SnapshotLoading_IgnoresHostTables()
        {
            var instanceId = Id();
            InsertInstance(instanceId, "seq-" + Id(), 0, ActionType.Debug);
            Execute("INSERT INTO action_instance_debug (action_instance_id) VALUES ('" + instanceId + "');");
            Execute("INSERT INTO unity_fake_action_binding (action_instance_id, payload) VALUES ('" + instanceId + "', 'payload-a');");

            var snapshot = GameContentSnapshotLoader.Load(_connection);
            Assert.IsNotEmpty(snapshot.Cards, "snapshot loads core content");

            Assert.AreEqual("payload-a", BindingPayload(instanceId));
            AssertRowCount("unity_fake_action_binding", 1);
        }

        // --- 8. Normal authoring never truncates/rebuilds core content --------

        [Test]
        public void NormalAuthoring_NeverTruncatesOrRebuildsCoreContent()
        {
            // Seed an unrelated, complete content island.
            var keptSession = Id();
            var keptPhase = Id();
            var keptInstance = Id();
            SessionRepository.Create(_connection, keptSession, "Kept", "type-standard");
            PhaseRepository.Create(_connection, keptPhase, "Kept Phase");
            InsertInstance(keptInstance, "seq-" + Id(), 0, ActionType.Debug);
            Execute("INSERT INTO unity_fake_action_binding (action_instance_id, payload) VALUES ('" + keptInstance + "', 'kept-binding');");

            // Full authoring cycle on a different session: create, edit, delete.
            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "Working", "type-standard");
            var p1 = Id();
            PhaseRepository.Create(_connection, p1, "P1");
            SessionRepository.Rename(_connection, sessionId, "Working 2");
            SessionRepository.Delete(_connection, sessionId);

            // The unrelated island is untouched, bit for bit.
            Assert.AreEqual("Kept", SessionTitle(keptSession));
            Assert.AreEqual("Kept Phase", PhaseTitle(keptPhase));
            Assert.AreEqual("kept-binding", BindingPayload(keptInstance));
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM phase WHERE id = '" + keptPhase + "';";
                Assert.AreEqual(1L, Convert.ToInt64(command.ExecuteScalar()));
            }
        }

        // --- Helpers -----------------------------------------------------------

        private string BindingPayload(string instanceId)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT payload FROM unity_fake_action_binding WHERE action_instance_id = '" + instanceId + "';";
                return (string)command.ExecuteScalar();
            }
        }

        private string SessionTitle(string sessionId)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT title FROM session WHERE id = '" + sessionId + "';";
                return (string)command.ExecuteScalar();
            }
        }

        private string PhaseTitle(string phaseId)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT title FROM phase WHERE id = '" + phaseId + "';";
                return (string)command.ExecuteScalar();
            }
        }

        private void AssertRowCount(string table, long expected)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
                Assert.AreEqual(expected, Convert.ToInt64(command.ExecuteScalar()));
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
