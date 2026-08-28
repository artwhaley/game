using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Core;
using TruthCardGame.Profile;

namespace TruthCardGame.Profile.Sqlite
{
    /// <summary>
    /// The profile store: opens/migrates UserProfile.db and persists the
    /// user's kink preferences, owned equipment, and available capabilities.
    /// Written against DbConnection/DbTransaction only — the host supplies
    /// the connection factory (WPF passes a Microsoft.Data.Sqlite factory;
    /// nothing here is provider-specific).
    /// </summary>
    public static class ProfileStore
    {
        /// <summary>Applies pending profile migrations; returns the current version.</summary>
        public static int EnsureSchema(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Execute(connection, transaction,
                        "CREATE TABLE IF NOT EXISTS profile_schema_migration (" +
                        "version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_utc TEXT NOT NULL);");

                    var applied = LoadAppliedVersions(connection, transaction);
                    var current = 0;
                    foreach (var migration in ProfileMigrations.All)
                    {
                        if (applied.Contains(migration.Version))
                        {
                            current = migration.Version;
                            continue;
                        }

                        foreach (var statement in SplitStatements(migration.Script))
                        {
                            Execute(connection, transaction, statement);
                        }

                        Execute(connection, transaction,
                            "INSERT INTO profile_schema_migration (version, name, applied_utc) " +
                            "VALUES (@version, @name, @appliedUtc);",
                            ("version", migration.Version), ("name", migration.Name),
                            ("appliedUtc", DateTime.UtcNow.ToString("o")));

                        current = migration.Version;
                    }

                    transaction.Commit();
                    return current;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Loads the whole profile. Missing rows stay missing (Unconfigured/not owned/unavailable).</summary>
        public static UserProfileData Load(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var profile = new UserProfileData();

            QueryAll(connection, "SELECT kink_id, preference FROM kink_preference;",
                reader => profile.KinkPreferences[reader.GetString(0)] = ParsePreference(reader.GetString(1)));

            QueryAll(connection, "SELECT equipment_id FROM equipment_owned;",
                reader => profile.OwnedEquipmentIds.Add(reader.GetString(0)));

            QueryAll(connection, "SELECT capability_id FROM smart_toy_capability_available;",
                reader => profile.AvailableCapabilityIds.Add(reader.GetString(0)));

            return profile;
        }

        /// <summary>Replaces the stored kink preference for one kink. Null preference removes the row (back to Unconfigured).</summary>
        public static void SetKinkPreference(DbConnection connection, string kinkId, KinkPreference? preference)
        {
            if (string.IsNullOrEmpty(kinkId)) throw new ArgumentException("Kink id required.", nameof(kinkId));

            if (preference == null)
            {
                Execute(connection, null, "DELETE FROM kink_preference WHERE kink_id = @id;", ("id", kinkId));
                return;
            }

            Execute(connection, null,
                "INSERT INTO kink_preference (kink_id, preference) VALUES (@id, @pref) " +
                "ON CONFLICT(kink_id) DO UPDATE SET preference = @pref;",
                ("id", kinkId), ("pref", PreferenceName(preference.Value)));
        }

        public static void SetEquipmentOwned(DbConnection connection, string equipmentId, bool owned)
        {
            if (string.IsNullOrEmpty(equipmentId)) throw new ArgumentException("Equipment id required.", nameof(equipmentId));

            if (owned)
            {
                Execute(connection, null,
                    "INSERT OR IGNORE INTO equipment_owned (equipment_id) VALUES (@id);", ("id", equipmentId));
            }
            else
            {
                Execute(connection, null, "DELETE FROM equipment_owned WHERE equipment_id = @id;", ("id", equipmentId));
            }
        }

        public static void SetCapabilityAvailable(DbConnection connection, string capabilityId, bool available)
        {
            if (string.IsNullOrEmpty(capabilityId)) throw new ArgumentException("Capability id required.", nameof(capabilityId));

            if (available)
            {
                Execute(connection, null,
                    "INSERT OR IGNORE INTO smart_toy_capability_available (capability_id) VALUES (@id);", ("id", capabilityId));
            }
            else
            {
                Execute(connection, null, "DELETE FROM smart_toy_capability_available WHERE capability_id = @id;", ("id", capabilityId));
            }
        }

        internal static string PreferenceName(KinkPreference preference)
        {
            switch (preference)
            {
                case KinkPreference.Love: return "love";
                case KinkPreference.Like: return "like";
                case KinkPreference.Torture: return "torture";
                case KinkPreference.DontConsent: return "dont_consent";
                default: throw new InvalidOperationException("Unknown preference " + preference);
            }
        }

        internal static KinkPreference ParsePreference(string value)
        {
            switch (value)
            {
                case "love": return KinkPreference.Love;
                case "like": return KinkPreference.Like;
                case "torture": return KinkPreference.Torture;
                case "dont_consent": return KinkPreference.DontConsent;
                default: throw new InvalidOperationException("Unknown preference value '" + value + "'.");
            }
        }

        private static HashSet<int> LoadAppliedVersions(DbConnection connection, DbTransaction transaction)
        {
            var applied = new HashSet<int>();
            QueryAll(connection, "SELECT version FROM profile_schema_migration;",
                reader => applied.Add(reader.GetInt32(0)));
            return applied;
        }

        private static void Execute(DbConnection connection, DbTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var parameter in parameters)
                {
                    var dbParameter = command.CreateParameter();
                    dbParameter.ParameterName = parameter.Name;
                    dbParameter.Value = parameter.Value ?? DBNull.Value;
                    command.Parameters.Add(dbParameter);
                }
                command.ExecuteNonQuery();
            }
        }

        private static void QueryAll(DbConnection connection, string sql, Action<DbDataReader> visit, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                foreach (var parameter in parameters)
                {
                    var dbParameter = command.CreateParameter();
                    dbParameter.ParameterName = parameter.Name;
                    dbParameter.Value = parameter.Value ?? DBNull.Value;
                    command.Parameters.Add(dbParameter);
                }
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) visit(reader);
                }
            }
        }

        private static IReadOnlyList<string> SplitStatements(string script)
        {
            // Same convention as CoreMigrator.SplitStatements: strip '--'
            // comment lines, accumulate, then split on ';'. The profile schema
            // contains no string literals with semicolons, so this is safe.
            var buffer = new System.Text.StringBuilder();
            foreach (var rawLine in script.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("--")) continue;
                buffer.AppendLine(line);
            }

            var statements = new List<string>();
            foreach (var statement in buffer.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = statement.Trim();
                if (trimmed.Length > 0) statements.Add(trimmed);
            }
            return statements;
        }
    }
}
