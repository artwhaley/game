using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 03 HARD gate: schema v2 creation/upgrade behaves per
    /// Docs/GraphWorkbench/03-schema-v2-design.md, including on a migrated copy
    /// of the real canonical database, with legacy rows handled exactly as the
    /// migration posture promises (slot rows cleared, other legacy rows left
    /// physically present but unused, unknown host tables untouched).
    /// </summary>
    [TestFixture]
    public class SchemaV2MigrationTests : IDisposable
    {
        private string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-t03-{Guid.NewGuid():N}.db");
            SqliteConnection.ClearAllPools();
        }

        [TearDown]
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private SqliteConnection OpenFresh()
        {
            var connection = new SqliteConnection("Data Source=" + _dbPath);
            connection.Open();
            ConnectionInitializer.Initialize(connection);
            return connection;
        }

        // ---------- fresh install ----------

        [Test]
        public void FreshDatabase_LandsAtCurrentVersion_WithSeededDefaults()
        {
            using (var connection = OpenFresh())
            {
                Assert.AreEqual(CoreMigrations.MaxVersion, CoreMigrator.EnsureSchema(connection));

                Assert.AreEqual("Standard", Scalar(connection, "SELECT title FROM session_type WHERE id='type-standard'"));
                Assert.AreEqual(50d, Convert.ToDouble(
                    Scalar(connection, "SELECT default_value FROM temperature_definition WHERE id='happiness'")));
                Assert.AreEqual(0L, Count(connection, "action_instance"));
                Assert.AreEqual(0L, Count(connection, "phase_graph_node"));
                Assert.AreEqual(0L, Count(connection, "session_graph_node"));
                // v3 WPF authoring layout tables exist on a fresh db.
                Assert.Greater(Count(connection, "sqlite_master"), 0);
                AssertTableExists(connection, "wpf_session_node_layout");
                AssertTableExists(connection, "wpf_phase_node_layout");
                AssertTableExists(connection, "wpf_viewport_state");
            }
        }

        [Test]
        public void ReRunningEnsureSchema_IsNoop()
        {
            using (var connection = OpenFresh())
            {
                Assert.AreEqual(CoreMigrations.MaxVersion, CoreMigrator.EnsureSchema(connection));
                Assert.AreEqual(CoreMigrations.MaxVersion, CoreMigrator.EnsureSchema(connection));
            }
        }

        // ---------- HARD gate: migrate a copy of the v1-era canonical DB ----------

        /// <summary>
        /// The live canonical DB is migration-v2 since Ticket 04; these migration
        /// proofs run against a preserved v1-era fixture of the same content so
        /// the upgrade path stays tested end-to-end.
        /// </summary>
        private static string V1FixturePath()
        {
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "DotNet", "Game.Content.Sqlite.Tests", "Fixtures", "GameContent-v1.db"));
        }

        [Test]
        public void CanonicalCopy_MigratesToV2_AndPassesIntegrityChecks()
        {
            var canonical = V1FixturePath();
            Assume.That(File.Exists(canonical), Is.True, "v1 fixture missing at " + canonical);

            File.Copy(canonical, _dbPath, overwrite: true);

            using (var connection = OpenFresh())
            {
                Assert.AreEqual(CoreMigrations.MaxVersion, CoreMigrator.EnsureSchema(connection));

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
                        Assert.IsFalse(reader.Read(), "migrated canonical copy has FK violations");
                    }
                }

                // Transformation happened everywhere:
                Assert.Greater(Count(connection, "session_graph_node"), 0, "sessions rebuilt as graphs");
                Assert.Greater(Count(connection, "phase_exit"), 0, "phases gained exported exits");
                Assert.Greater(Count(connection, "action_instance"), 0, "card actions became instances");

                // Every session got a type; every temperature definition exists once.
                Assert.AreEqual(0L, Count(connection, "FROM session WHERE session_type_id IS NULL"));
                Assert.AreEqual(1L, Count(connection, "temperature_definition"));
            }
        }

        [Test]
        public void CanonicalCopy_TransformsSampleContent_Exactly()
        {
            var canonical = V1FixturePath();
            Assume.That(File.Exists(canonical), Is.True);

            File.Copy(canonical, _dbPath, overwrite: true);

            long expectedReferences;
            using (var preRead = new SqliteConnection("Data Source=" + _dbPath))
            {
                preRead.Open();
                expectedReferences = Convert.ToInt64(Scalar(preRead,
                    "SELECT COUNT(*) FROM phase_slot ps WHERE EXISTS (SELECT 1 FROM phase_slot_candidate psc WHERE psc.phase_slot_id = ps.id)"));
            }

            using (var connection = OpenFresh())
            {
                CoreMigrator.EnsureSchema(connection);

                // 6 phases -> 12 standard exits + 6 phases × 6 low-graph nodes.
                Assert.AreEqual(6L, Count(connection, "phase"));
                Assert.AreEqual(12L, Count(connection, "phase_exit"), "two standard exits per phase");
                Assert.AreEqual(36L, Count(connection, "phase_graph_node"), "six nodes per standard graph");
                Assert.AreEqual(36L, Count(connection, "phase_graph_edge"), "one outgoing edge per used output socket");

                // Cards carry owned sequences with appended default progress.
                var cardsWithSequence = Convert.ToInt64(Scalar(connection,
                    "SELECT COUNT(*) FROM card WHERE action_sequence_id IS NOT NULL"));
                Assert.AreEqual(Count(connection, "card"), cardsWithSequence);

                // Every transformed card ends with the default increment_progress(+10).
                var autoProgress = Convert.ToInt64(Scalar(connection,
                    "SELECT COUNT(*) FROM action_instance ai " +
                    "JOIN card c ON c.action_sequence_id = ai.action_sequence_id " +
                    "WHERE ai.action_type='increment_progress' AND ai.id LIKE 'ci-%-autoprogress' AND EXISTS (" +
                    "SELECT 1 FROM action_instance_increment_progress p WHERE p.action_instance_id = ai.id AND p.amount=10)"));
                Assert.AreEqual(cardsWithSequence, autoProgress);

                // Every legacy first-candidate placement became one reference node.
                Assert.Greater(expectedReferences, 0);
                Assert.AreEqual(expectedReferences, Count(connection, "session_node_phase"),
                    "rebuilt references match the legacy first-candidate placements");

                // 2 sessions × (start + end) + their rebuilt references:
                var sessions = Count(connection, "session");
                var references = Count(connection, "session_node_phase");
                Assert.Greater(references, 0, "sample sessions reference phases");
                Assert.AreEqual(2 * sessions + references, Count(connection, "session_graph_node"),
                    "exactly one Start and one End plus the rebuilt references");
                Assert.AreEqual(references, Count(connection, "session_graph_node WHERE node_type='PhaseReference'"));
            }
        }

        [Test]
        public void LegacyRows_BehavePerPosture_SlotsCleared_OthersKept_UnknownHostUntouched()
        {
            var canonical = V1FixturePath();
            Assume.That(File.Exists(canonical), Is.True);

            File.Copy(canonical, _dbPath, overwrite: true);

            long legacyActionsBefore, cardActionRowsBefore, optionRowsBefore;
            using (var setup = new SqliteConnection("Data Source=" + _dbPath))
            {
                setup.Open();
                Execute(setup,
                    "CREATE TABLE IF NOT EXISTS unity_fake_action_binding (" +
                    "resource_id TEXT PRIMARY KEY, asset_guid TEXT NOT NULL);");
                Execute(setup, "INSERT INTO unity_fake_action_binding VALUES ('r','guid-x');");

                legacyActionsBefore = Count(setup, "action");
                cardActionRowsBefore = Count(setup, "card_action");
                optionRowsBefore = Count(setup, "choice_option");
            }

            using (var connection = OpenFresh())
            {
                CoreMigrator.EnsureSchema(connection);

                Assert.AreEqual(legacyActionsBefore, Count(connection, "action"),
                    "top-level configured-action rows remain physically present (ignored)");
                Assert.AreEqual(cardActionRowsBefore, Count(connection, "card_action"));
                Assert.AreEqual(optionRowsBefore, Count(connection, "choice_option"));

                Assert.AreEqual(0L, Count(connection, "phase_slot"), "slot rows cleared");
                Assert.AreEqual(0L, Count(connection, "phase_slot_candidate"), "candidate rows cleared");

                Assert.AreEqual(1L, Count(connection, "unity_fake_action_binding"),
                    "unknown host tables survive core migrations untouched");
            }
        }

        // ---------- constraint behavior proofs ----------

        [Test]
        public void DuplicateInstanceOrdinal_InOneSequence_IsRejected()
        {
            using (var connection = OpenFresh())
            {
                CoreMigrator.EnsureSchema(connection);
                Execute(connection, "INSERT INTO action_sequence VALUES ('seq');");
                Instance(connection, "a1", "seq", 0, ActionType.Debug);
                Instance(connection, "a2", "seq", 1, ActionType.ReturnV2);

                var failure = Assert.Throws<SqliteException>(() =>
                    Instance(connection, "a3", "seq", 0, ActionType.StatIncrease));
                StringAssert.Contains("UNIQUE", failure.Message.ToUpperInvariant());
            }
        }

        [Test]
        public void TwoOutgoingEdges_FromOneOutput_AreRejected_AtBothGraphLevels()
        {
            using (var connection = OpenFresh())
            {
                CoreMigrator.EnsureSchema(connection);

                // Phase level
                Execute(connection, "INSERT INTO phase VALUES ('p1','Phase',0,0);");
                Execute(connection, "INSERT INTO phase_graph_node VALUES ('pn1','p1','Entry');");
                Execute(connection, "INSERT INTO phase_graph_node VALUES ('pn2','p1','CardExecutor');");
                Execute(connection, "INSERT INTO phase_node_output VALUES ('o1','pn1','normal',0,NULL);");
                Edge(connection, "peA", "p1", "o1", "pn2");
                var failure = Assert.Throws<SqliteException>(() => Edge(connection, "peB", "p1", "o1", "pn2"));
                StringAssert.Contains("UNIQUE", failure.Message.ToUpperInvariant());

                // Session level
                Execute(connection, "INSERT INTO session VALUES ('s1','Session',NULL);");
                Execute(connection, "INSERT INTO session_graph_node VALUES ('sn1','s1','Start');");
                Execute(connection, "INSERT INTO session_graph_node VALUES ('sn2','s1','End');");
                Execute(connection,
                    "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) VALUES ('so1','sn1','normal',0,NULL,NULL,NULL);");
                SEdge(connection, "seA", "s1", "so1", "sn2");
                failure = Assert.Throws<SqliteException>(() => SEdge(connection, "seB", "s1", "so1", "sn2"));
                StringAssert.Contains("UNIQUE", failure.Message.ToUpperInvariant());
            }
        }

        [Test]
        public void DeletingSequence_CascadesInstancesAndSubtypeRows()
        {
            using (var connection = OpenFresh())
            {
                CoreMigrator.EnsureSchema(connection);
                Execute(connection, "INSERT INTO action_sequence VALUES ('seq');");
                Instance(connection, "i1", "seq", 0, ActionType.ModifyTemperatureV2);
                SubType(connection,
                    "INSERT INTO action_instance_modify_temperature VALUES ('i1','happiness',5);");

                Assert.AreEqual(1L, Count(connection, "action_instance"));
                Assert.AreEqual(1L, Count(connection, "action_instance_modify_temperature"));

                Execute(connection, "DELETE FROM action_sequence WHERE id='seq';");

                Assert.AreEqual(0L, Count(connection, "action_instance"));
                Assert.AreEqual(0L, Count(connection, "action_instance_modify_temperature"));
            }
        }

        [Test]
        public void DeletingReferencedPhase_IsBlocked_DeletingUnreferencedPhase_CascadesItsGraph()
        {
            using (var connection = OpenFresh())
            {
                CoreMigrator.EnsureSchema(connection);

                Execute(connection, "INSERT INTO phase VALUES ('free','Free',0,0);");
                Execute(connection, "INSERT INTO phase VALUES ('used','Used',0,0);");
                foreach (var phase in new[] { "free", "used" })
                {
                    Execute(connection, $"INSERT INTO phase_graph_node VALUES ('n-{phase}','{phase}','Entry');");
                    Execute(connection, $"INSERT INTO phase_exit VALUES ('px-{phase}-complete','{phase}',0,'Complete');");
                }

                Execute(connection, "INSERT INTO session VALUES ('s1','S',NULL);");
                Execute(connection, "INSERT INTO session_graph_node VALUES ('ref','s1','PhaseReference');");
                Execute(connection, "INSERT INTO session_node_phase VALUES ('ref','used');");

                // Referenced -> RESTRICT blocks the delete.
                Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM phase WHERE id='used';"));

                // Unreferenced -> its low graph/exits cascade away; slot structures are gone.
                Execute(connection, "DELETE FROM phase WHERE id='free';");
                Assert.AreEqual(0L, Count(connection, "phase_graph_node WHERE phase_id='free'"));
                Assert.AreEqual(0L, Count(connection, "phase_exit WHERE phase_id='free'"));

                // Referencing session survives; deleting IT cascades the placement...
                Execute(connection, "DELETE FROM session WHERE id='s1';");
                Assert.AreEqual(0L, Count(connection, "session_node_phase"));
                // ...but the referenced Phase itself is untouched.
                Assert.AreEqual(1L, Count(connection, "phase WHERE id='used'"));
            }
        }

        [Test]
        public void MissingTemperatureOrResourceReferences_AreRejected_ByPhysicalForeignKeys()
        {
            using (var connection = OpenFresh())
            {
                CoreMigrator.EnsureSchema(connection);
                Execute(connection, "INSERT INTO action_sequence VALUES ('seq');");
                Instance(connection, "mt", "seq", 0, ActionType.ModifyTemperatureV2);

                var failure = Assert.Throws<SqliteException>(() => SubType(connection,
                    "INSERT INTO action_instance_modify_temperature VALUES ('mt','no-such-temp',1);"));
                StringAssert.Contains("FOREIGN KEY", failure.Message.ToUpperInvariant());

                Execute(connection, "INSERT INTO resource VALUES ('res','cutscene','Res');");
                Instance(connection, "cs", "seq", 1, ActionType.Cutscene);
                failure = Assert.Throws<SqliteException>(() => SubType(connection,
                    "INSERT INTO action_instance_cutscene VALUES ('cs','no-such-resource');"));
                StringAssert.Contains("FOREIGN KEY", failure.Message.ToUpperInvariant());
            }
        }

        [Test]
        public void DeletingExit_CascadesProjectedPlacementSocket_EdgeSurvivesByRemovingTheEdgeToo()
        {
            using (var connection = OpenFresh())
            {
                CoreMigrator.EnsureSchema(connection);

                Execute(connection, "INSERT INTO phase VALUES ('p','P',0,0);");
                Execute(connection, "INSERT INTO phase_exit VALUES ('px-p-fail','p',0,'Fail');");
                Execute(connection, "INSERT INTO session VALUES ('s','S',NULL);");
                Execute(connection, "INSERT INTO session_graph_node VALUES ('ref','s','PhaseReference');");
                Execute(connection, "INSERT INTO session_node_phase VALUES ('ref','p');");
                Execute(connection, "INSERT INTO session_graph_node VALUES ('end','s','End');");
                Execute(connection,
                    "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) VALUES ('sock','ref','phase_exit',0,NULL,'px-p-fail',NULL);");
                SEdge(connection, "se1", "s", "sock", "end");

                // Removing the exit cascades the projected socket and its edge rows
                // (v2 repositories use this exact behavior for live projection updates).
                Execute(connection, "DELETE FROM phase_exit WHERE id='px-p-fail';");
                Assert.AreEqual(0L, Count(connection, "session_node_output WHERE id='sock'"));
                Assert.AreEqual(0L, Count(connection, "session_graph_edge WHERE id='se1'"));
            }
        }

        // ---------- helpers ----------

        private static void AssertTableExists(SqliteConnection connection, string name)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
                command.Parameters.AddWithValue("@name", name);
                Assert.AreEqual(1L, Convert.ToInt64(command.ExecuteScalar()), "table missing: " + name);
            }
        }

        private static object Scalar(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
        }

        private static long Count(SqliteConnection connection, string fromClause)
        {
            return Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) " +
                (fromClause.TrimStart().StartsWith("FROM", StringComparison.OrdinalIgnoreCase)
                    ? fromClause
                    : "FROM " + fromClause)));
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static void Instance(SqliteConnection connection, string id, string seqId, int ordinal, string type)
        {
            InstanceRow(connection, id, seqId, ordinal, type);
        }

        private static void InstanceRow(SqliteConnection connection, string id, string seqId, int ordinal, string type)
        {
            Execute(connection,
                "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                $"VALUES ('{id}','{seqId}',{ordinal},'{type}',{(type == ActionType.PhaseGotoV2 || type == ActionType.SessionGotoV2 || type == ActionType.ReturnV2 || type == ActionType.EndSessionV2 ? 1 : 0)});");
        }

        private static void SubType(SqliteConnection connection, string sql)
        {
            Execute(connection, sql);
        }

        private static void Edge(SqliteConnection connection, string id, string phaseId, string sourcePortId, string targetNodeId)
        {
            Execute(connection,
                "INSERT INTO phase_graph_edge (id, phase_id, source_port_id, target_node_id) " +
                $"VALUES ('{id}','{phaseId}','{sourcePortId}','{targetNodeId}');");
        }

        private static void SEdge(SqliteConnection connection, string id, string sessionId, string sourcePortId, string targetNodeId)
        {
            Execute(connection,
                "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) " +
                $"VALUES ('{id}','{sessionId}','{sourcePortId}','{targetNodeId}');");
        }
    }
}
