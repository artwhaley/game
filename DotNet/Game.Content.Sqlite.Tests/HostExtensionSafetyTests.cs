using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 11: prove the core DB is safe for host extensions (Unity/WPF).
    /// A test-only extension table — the exact shape a Unity binding layer
    /// would own — must survive every ordinary authoring operation, cascade
    /// only on intentional action deletes, never be rebuilt by core
    /// migrations, and be invisible to snapshot loading.
    /// </summary>
    [TestFixture]
    public class HostExtensionSafetyTests
    {
        private const string CreateHostTable =
            "CREATE TABLE IF NOT EXISTS unity_fake_action_binding (" +
            "  action_id TEXT PRIMARY KEY, " +
            "  payload TEXT NOT NULL, " +
            "  FOREIGN KEY(action_id) REFERENCES action(id) ON DELETE CASCADE" +
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

        // --- 1. In-place Action update preserves binding ----------------------

        [Test]
        public void UpdateActionInPlace_PreservesHostBinding()
        {
            var actionId = Id();
            Execute("INSERT INTO action (id, name, action_type, is_blocking) VALUES ('" + actionId + "', 'Old', 'debug', 0);");
            InsertBinding(actionId, "payload-a");

            Execute("UPDATE action SET name = 'New', is_blocking = 1 WHERE id = '" + actionId + "';");

            Assert.AreEqual("payload-a", BindingPayload(actionId));
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM action WHERE id = '" + actionId + "';";
                Assert.AreEqual("New", command.ExecuteScalar());
            }
        }

        // --- 2. Unrelated Session/Phase edits preserve bindings ---------------

        [Test]
        public void UnrelatedSessionAndPhaseEdits_PreserveHostBinding()
        {
            var actionId = Id();
            Execute("INSERT INTO action (id, action_type, is_blocking) VALUES ('" + actionId + "', 'debug', 0);");
            InsertBinding(actionId, "payload-a");

            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "S", new[] { "intense" });
            SessionRepository.UpdateTitle(_connection, sessionId, "S2");
            SessionRepository.ReplaceTags(_connection, sessionId, new[] { "relaxing" });

            var phaseId = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = phaseId, Title = "P", MustIncludeTags = { "solo" } });
            PhaseRepository.Update(_connection, phaseId, "P2", 2, 4);
            PhaseRepository.ReplaceMustExcludeTags(_connection, phaseId, new[] { "dare" });

            Assert.AreEqual("payload-a", BindingPayload(actionId));
        }

        // --- 3. Card-action reorder preserves Action rows/bindings ------------

        [Test]
        public void CardActionReorder_PreservesActionRowsAndBindings()
        {
            var a1 = Id();
            var a2 = Id();
            Execute("INSERT INTO action (id, action_type, is_blocking) VALUES ('" + a1 + "', 'debug', 0), ('" + a2 + "', 'debug', 0);");
            InsertBinding(a1, "b1");
            InsertBinding(a2, "b2");

            var cardId = Id();
            Execute("INSERT INTO card (id, title) VALUES ('" + cardId + "', 'C');");
            Execute("INSERT INTO card_action (card_id, ordinal, action_id) VALUES ('" + cardId + "', 0, '" + a1 + "'), ('" + cardId + "', 1, '" + a2 + "');");

            // Swap ordinals safely: move both to unique temp ordinals, then to final positions.
            using (var transaction = _connection.BeginTransaction())
            {
                Execute("UPDATE card_action SET ordinal = 90 WHERE card_id = '" + cardId + "' AND ordinal = 0;");
                Execute("UPDATE card_action SET ordinal = 91 WHERE card_id = '" + cardId + "' AND ordinal = 1;");
                Execute("UPDATE card_action SET ordinal = 0 WHERE card_id = '" + cardId + "' AND action_id = '" + a2 + "';");
                Execute("UPDATE card_action SET ordinal = 1 WHERE card_id = '" + cardId + "' AND action_id = '" + a1 + "';");
                transaction.Commit();
            }

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT action_id FROM card_action WHERE card_id = '" + cardId + "' ORDER BY ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    Assert.IsTrue(reader.Read());
                    Assert.AreEqual(a2, reader.GetString(0));
                    Assert.IsTrue(reader.Read());
                    Assert.AreEqual(a1, reader.GetString(0));
                    Assert.IsFalse(reader.Read());
                }
            }
            Assert.AreEqual("b1", BindingPayload(a1));
            Assert.AreEqual("b2", BindingPayload(a2));
            AssertRowCount("action", 2);
        }

        // --- 4. PhaseSlot reorder preserves Phase rows ------------------------

        [Test]
        public void PhaseSlotReorder_PreservesPhaseRows()
        {
            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "S", null);
            var p1 = Id();
            var p2 = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = p1, Title = "P1", MinCards = 1, MaxCards = 3 });
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = p2, Title = "P2", MinCards = 2, MaxCards = 4 });
            var slotA = Id();
            var slotB = Id();
            PhaseSlotRepository.Create(_connection, sessionId, slotA, "A");
            PhaseSlotRepository.Create(_connection, sessionId, slotB, "B");
            PhaseSlotRepository.AddCandidate(_connection, slotA, Id(), p1);
            PhaseSlotRepository.AddCandidate(_connection, slotB, Id(), p2);

            PhaseSlotRepository.Reorder(_connection, sessionId, new[] { slotB, slotA });

            Assert.AreEqual("P1", PhaseRepository.Get(_connection, p1).Title);
            Assert.AreEqual("P2", PhaseRepository.Get(_connection, p2).Title);
            AssertRowCount("phase", 2);
        }

        // --- 5. Intentional Action delete cascades owned host binding ---------

        [Test]
        public void IntentionalActionDelete_CascadesOwnedHostBinding()
        {
            var actionId = Id();
            Execute("INSERT INTO action (id, action_type, is_blocking) VALUES ('" + actionId + "', 'debug', 0);");
            InsertBinding(actionId, "payload-a");

            // Scoped, parameterized delete — the only delete pattern the codebase uses.
            using (var transaction = _connection.BeginTransaction())
            {
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM action WHERE id = @id;";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@id";
                    parameter.Value = actionId;
                    command.Parameters.Add(parameter);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            AssertRowCount("action", 0);
            AssertRowCount("unity_fake_action_binding", 0);
        }

        // --- 6. Referenced Action delete is blocked until core refs removed ---

        [Test]
        public void ReferencedActionDelete_IsBlocked_UntilCoreRefsRemoved()
        {
            var actionId = Id();
            Execute("INSERT INTO action (id, action_type, is_blocking) VALUES ('" + actionId + "', 'debug', 0);");
            InsertBinding(actionId, "payload-a");

            var cardId = Id();
            Execute("INSERT INTO card (id, title) VALUES ('" + cardId + "', 'C');");
            Execute("INSERT INTO card_action (card_id, ordinal, action_id) VALUES ('" + cardId + "', 0, '" + actionId + "');");

            Assert.Throws<SqliteException>(() => Execute("DELETE FROM action WHERE id = '" + actionId + "';"));
            AssertRowCount("action", 1);

            // Remove the core reference, then the intentional delete succeeds and cascades the binding.
            Execute("DELETE FROM card_action WHERE card_id = '" + cardId + "' AND ordinal = 0;");
            Execute("DELETE FROM action WHERE id = '" + actionId + "';");
            AssertRowCount("action", 0);
            AssertRowCount("unity_fake_action_binding", 0);
        }

        // --- 7. Core migrations preserve unknown host tables ------------------

        [Test]
        public void CoreMigrations_PreserveUnknownHostTables()
        {
            // Simulate the real extension-first upgrade path: core v1 applied,
            // host extension installs its binding table, then an older core
            // (empty migration ledger) re-applies the migration on top. The
            // host table and its rows must survive untouched.
            Execute("DELETE FROM core_schema_migration;");
            var actionId = Id();
            Execute("INSERT INTO action (id, action_type, is_blocking) VALUES ('" + actionId + "', 'debug', 0);");
            InsertBinding(actionId, "kept");

            CoreMigrator.EnsureSchema(_connection);

            AssertRowCount("unity_fake_action_binding", 1);
            Assert.AreEqual("kept", BindingPayload(actionId));

            // Idempotent re-run also leaves the host table and its rows alone.
            CoreMigrator.EnsureSchema(_connection);
            Assert.AreEqual("kept", BindingPayload(actionId));
        }

        // --- 8. Snapshot loading ignores host tables --------------------------

        [Test]
        public void SnapshotLoading_IgnoresHostTables()
        {
            var actionId = Id();
            Execute("INSERT INTO action (id, action_type, is_blocking) VALUES ('" + actionId + "', 'debug', 0);");
            Execute("INSERT INTO action_debug (action_id) VALUES ('" + actionId + "');");
            InsertBinding(actionId, "payload-a");

            var snapshot = GameContentSnapshotLoader.Load(_connection);
            Assert.IsNotEmpty(snapshot.Actions);

            Assert.AreEqual("payload-a", BindingPayload(actionId));
            AssertRowCount("unity_fake_action_binding", 1);
        }

        // --- 9. Normal authoring never truncates/rebuilds core content --------

        [Test]
        public void NormalAuthoring_NeverTruncatesOrRebuildsCoreContent()
        {
            // Seed an unrelated, complete content island.
            var keptSession = Id();
            var keptPhase = Id();
            var keptAction = Id();
            var keptSlot = Id();
            SessionRepository.Create(_connection, keptSession, "Kept", new[] { "intense" });
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = keptPhase, Title = "Kept Phase", MinCards = 1, MaxCards = 3, MustIncludeTags = { "solo" } });
            Execute("INSERT INTO action (id, action_type, is_blocking) VALUES ('" + keptAction + "', 'debug', 0);");
            InsertBinding(keptAction, "kept-binding");
            PhaseSlotRepository.Create(_connection, keptSession, keptSlot, "Kept Slot");
            PhaseSlotRepository.AddCandidate(_connection, keptSlot, Id(), keptPhase);

            // Full authoring cycle on a different session: create, edit, reorder,
            // replace tags, add/remove candidates, delete.
            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "Working", new[] { "new" });
            var p1 = Id();
            var p2 = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = p1, Title = "P1" });
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = p2, Title = "P2" });
            var slotA = Id();
            var slotB = Id();
            PhaseSlotRepository.Create(_connection, sessionId, slotA, "A");
            PhaseSlotRepository.Create(_connection, sessionId, slotB, "B");
            var candA = Id();
            var candB = Id();
            PhaseSlotRepository.AddCandidate(_connection, slotA, candA, p1);
            PhaseSlotRepository.AddCandidate(_connection, slotB, candB, p2);
            PhaseSlotRepository.Reorder(_connection, sessionId, new[] { slotB, slotA });
            PhaseSlotRepository.ReorderCandidates(_connection, slotB, new[] { candB });
            SessionRepository.UpdateTitle(_connection, sessionId, "Working 2");
            SessionRepository.ReplaceTags(_connection, sessionId, new[] { "intense", "dare" });
            PhaseRepository.ReplaceMustIncludeTags(_connection, p1, new[] { "party" });
            SessionRepository.Delete(_connection, sessionId);

            // The unrelated island is untouched, bit for bit.
            Assert.AreEqual("Kept", SessionRepository.Get(_connection, keptSession).Title);
            Assert.AreEqual("Kept Phase", PhaseRepository.Get(_connection, keptPhase).Title);
            Assert.AreEqual("kept-binding", BindingPayload(keptAction));
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM phase WHERE id = '" + keptPhase + "';";
                Assert.AreEqual(1L, Convert.ToInt64(command.ExecuteScalar()));
            }
        }

        // --- Helpers -----------------------------------------------------------

        private void InsertBinding(string actionId, string payload) =>
            Execute("INSERT INTO unity_fake_action_binding (action_id, payload) VALUES ('" + actionId + "', '" + payload + "');");

        private string BindingPayload(string actionId)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT payload FROM unity_fake_action_binding WHERE action_id = '" + actionId + "';";
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

        private static void ExecuteOn(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static object ScalarOn(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
        }
    }
}
