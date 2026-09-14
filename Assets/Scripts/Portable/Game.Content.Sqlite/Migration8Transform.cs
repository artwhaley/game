using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Migration 8's in-transaction data transformation (Docs/ToyPatternDialog/
    /// SCHEMA-V8-DIRECTION.md). Runs after every DDL statement of
    /// SQLITE-SCHEMA-V8-TOY-DIALOG.sql, inside the same transaction.
    ///
    /// Rebuilds action_instance_toy_activity: the legacy intensity column is
    /// replaced by a pattern_resource_id reference. While the legacy rows are
    /// still readable:
    /// - zero legacy rows: rebuild cleanly, no pattern seeding;
    /// - rows exist: for each distinct intensity value create one deterministic
    ///   constant `toy_pattern` Resource ("Migrated Constant 50%" naming), then
    ///   repoint each row at the Resource for its intensity. Action Instance
    ///   IDs, capabilities, durations, and blocking flags are preserved.
    ///
    /// No silent intensity loss. Host tables (unity_*, wpf_*) are never touched.
    /// </summary>
    internal static class Migration8Transform
    {
        public static void Transform(DbConnection connection, DbTransaction transaction)
        {
            var legacy = new List<(string InstanceId, string CapabilityId, double Intensity, double Duration)>();

            Sql.QueryAll(connection, transaction,
                "SELECT action_instance_id, capability_id, intensity, duration_seconds FROM action_instance_toy_activity;",
                reader => legacy.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3))));

            // Distinct canonical intensity -> deterministic constant-pattern Resource.
            var resourceByIntensity = new Dictionary<double, string>();
            foreach (var row in legacy)
            {
                var key = row.Intensity;
                if (resourceByIntensity.ContainsKey(key)) continue;

                var resourceId = "res-toy-const-bits-" + FormatIntensity(key);
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO resource (id, kind, name) VALUES (@id, @kind, @name);",
                    ("id", resourceId),
                    ("kind", ResourceKinds.ToyPattern),
                    ("name", "Migrated Constant " + FormatPercent(key)));

                resourceByIntensity[key] = resourceId;
            }

            // Rebuild the table with the new shape (SQLite has no DROP COLUMN).
            Sql.Execute(connection, transaction,
                "CREATE TABLE action_instance_toy_activity_v8 (" +
                "action_instance_id  TEXT PRIMARY KEY," +
                "capability_id       TEXT NOT NULL," +
                "pattern_resource_id TEXT NOT NULL," +
                "duration_seconds    REAL NOT NULL DEFAULT 0 CHECK (duration_seconds >= 0)," +
                "FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE," +
                "FOREIGN KEY (capability_id) REFERENCES smart_toy_capability_definition(id) ON DELETE RESTRICT," +
                "FOREIGN KEY (pattern_resource_id) REFERENCES resource(id) ON DELETE RESTRICT);");

            foreach (var row in legacy)
            {
                Sql.Execute(connection, transaction,
                    "INSERT INTO action_instance_toy_activity_v8 " +
                    "(action_instance_id, capability_id, pattern_resource_id, duration_seconds) " +
                    "VALUES (@i, @capability, @pattern, @duration);",
                    ("i", row.InstanceId),
                    ("capability", row.CapabilityId),
                    ("pattern", resourceByIntensity[row.Intensity]),
                    ("duration", row.Duration));
            }

            Sql.Execute(connection, transaction, "DROP TABLE action_instance_toy_activity;");
            Sql.Execute(connection, transaction,
                "ALTER TABLE action_instance_toy_activity_v8 RENAME TO action_instance_toy_activity;");

            // Invariant hygiene: every Session must own a default card-weighting
            // row so the canonical guard (MigratesCopy_ToCurrentMigration) and card
            // selection never see a session without weighting. Migration5 seeded
            // this for its epoch, but any Session created since (e.g. the live
            // canonical at v7) reaches v8 without re-running v5, so v8 re-seeds.
            Sql.Execute(connection, transaction,
                "INSERT OR IGNORE INTO session_card_weighting (session_id) SELECT id FROM session;");
        }

        /// <summary>"50" for 0.5 — stable across cultures.</summary>
        private static string FormatPercent(double intensity)
        {
            return Math.Round(intensity * 100d).ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatIntensity(double intensity)
        {
            // Friendly decimal formatting is not an identity: 0.501 and 0.504
            // both collapse to the same rounded label. Use exact IEEE-754 bits
            // for deterministic, collision-free legacy resource IDs.
            return BitConverter.DoubleToInt64Bits(intensity).ToString("X16", CultureInfo.InvariantCulture);
        }
    }
}
