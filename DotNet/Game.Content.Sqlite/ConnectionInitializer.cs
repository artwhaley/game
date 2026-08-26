using System;
using System.Data;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Applies the SQLite connection-level pragmas required before any
    /// transactional work (architecture contract §13): foreign key
    /// enforcement on, bounded busy timeout, and a hard verification that
    /// foreign keys are actually enabled. Provider-neutral — works on any
    /// <see cref="DbConnection"/>.
    /// </summary>
    public static class ConnectionInitializer
    {
        public const int BusyTimeoutMs = 5000;

        public static void Initialize(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            Execute(connection, "PRAGMA foreign_keys = ON;");
            Execute(connection, "PRAGMA busy_timeout = " + BusyTimeoutMs + ";");

            if (!ForeignKeysEnabled(connection))
            {
                throw new InvalidOperationException(
                    "SQLite foreign key enforcement could not be enabled on the connection.");
            }
        }

        private static void Execute(DbConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static bool ForeignKeysEnabled(DbConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys;";
                var value = command.ExecuteScalar();
                return value != null && Convert.ToInt64(value) == 1L;
            }
        }
    }
}
