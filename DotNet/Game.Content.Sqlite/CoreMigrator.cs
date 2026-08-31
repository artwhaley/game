using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Small ordered core migrator: ensures the <c>core_schema_migration</c>
    /// table, applies pending migrations in version order (one transaction
    /// per migration), and is a no-op on re-run. Never drops or alters tables
    /// it does not own, so host extension tables (unity_*, wpf_*) survive.
    /// </summary>
    public static class CoreMigrator
    {
        public static int EnsureSchema(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            EnsureMigrationTable(connection);
            var applied = LoadAppliedVersions(connection);

            var current = 0;
            foreach (var migration in CoreMigrations.All)
            {
                if (applied.Contains(migration.Version))
                {
                    current = migration.Version;
                    continue;
                }

                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var statement in SplitStatements(migration.Script))
                    {
                        Execute(connection, transaction, statement);
                    }

                    // Data transformation (migration 2) runs inside the same
                    // transaction so DDL + row work commit or roll back together.
                    migration.Callback?.Invoke(connection, transaction);

                    Execute(connection, transaction,
                        "INSERT INTO core_schema_migration (version, name, applied_utc) " +
                        "VALUES (@version, @name, @appliedUtc);",
                        new SqlParameter("version", migration.Version),
                        new SqlParameter("name", migration.Name),
                        new SqlParameter("appliedUtc", DateTime.UtcNow.ToString("o")));

                    transaction.Commit();
                }

                current = migration.Version;
            }

            return current;
        }

        private static void EnsureMigrationTable(DbConnection connection)
        {
            Execute(connection, null,
                "CREATE TABLE IF NOT EXISTS core_schema_migration (" +
                "version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_utc TEXT NOT NULL);");
        }

        private static HashSet<int> LoadAppliedVersions(DbConnection connection)
        {
            var applied = new HashSet<int>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT version FROM core_schema_migration;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        applied.Add(reader.GetInt32(0));
                    }
                }
            }
            return applied;
        }

        private static void Execute(DbConnection connection, DbTransaction transaction, string sql, params SqlParameter[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var parameter in parameters)
                {
                    var dbParameter = command.CreateParameter();
                    dbParameter.ParameterName = parameter.Name;
                    dbParameter.Value = parameter.Value;
                    command.Parameters.Add(dbParameter);
                }
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Splits a DDL script into individual statements. Strips '--' line
        /// comments; ordinary statements are ';'-terminated while SQLite
        /// trigger bodies remain intact through their final END;. The core
        /// schema contains no string literals with semicolons, so this small
        /// parser is sufficient for the embedded migration scripts.
        /// </summary>
        internal static IReadOnlyList<string> SplitStatements(string script)
        {
            var buffer = new StringBuilder();
            foreach (var rawLine in script.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("--")) continue;
                buffer.AppendLine(line);
            }

            var statements = new List<string>();
            var current = new StringBuilder();
            foreach (var part in buffer.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedPart = part.Trim();
                if (trimmedPart.Length == 0) continue;

                if (current.Length > 0) current.Append(';');
                current.Append(trimmedPart);

                // SQLite trigger bodies contain their own semicolon-terminated
                // statements. Keep those together until the body's END; rather
                // than handing an incomplete trigger to the command engine.
                var candidate = current.ToString().Trim();
                var isTrigger = candidate.StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase) ||
                                candidate.StartsWith("CREATE TEMP TRIGGER", StringComparison.OrdinalIgnoreCase) ||
                                candidate.StartsWith("CREATE TEMPORARY TRIGGER", StringComparison.OrdinalIgnoreCase);
                if (isTrigger && !string.Equals(trimmedPart, "END", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                statements.Add(candidate);
                current.Clear();
            }

            var trailing = current.ToString().Trim();
            if (trailing.Length > 0) statements.Add(trailing);
            return statements;
        }

        private sealed class SqlParameter
        {
            public SqlParameter(string name, object value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }
            public object Value { get; }
        }
    }
}
