using System;
using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>Internal helpers shared by the authoring repositories.</summary>
    internal static class Sql
    {
        public static void Execute(DbConnection connection, DbTransaction transaction, string sql, params (string Name, object Value)[] parameters)
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

        public static void QueryAll(DbConnection connection, string sql, Action<DbDataReader> visit, params (string Name, object Value)[] parameters)
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

        /// <summary>
        /// Ensures a tag row exists for each name. Tag ids are the tag names
        /// themselves (name is UNIQUE), matching the initializer convention.
        /// </summary>
        public static void EnsureTags(DbConnection connection, DbTransaction transaction, IReadOnlyList<string> names)
        {
            if (names == null) return;
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                Execute(connection, transaction,
                    "INSERT OR IGNORE INTO tag (id, name) VALUES (@id, @name);",
                    ("id", name), ("name", name));
            }
        }

        /// <summary>
        /// Reassigns ordinals of a (parent, ordinal)-keyed ordered table
        /// without violating the unique constraint midway: shift every row by
        /// a large offset first, then stamp the new order. The caller's list
        /// must contain every row of the parent (full reorder).
        /// </summary>
        public static void Reorder(
            DbConnection connection,
            DbTransaction transaction,
            string table,
            string parentColumn,
            string parentId,
            string idColumn,
            IReadOnlyList<string> orderedIds,
            int expectedCount)
        {
            if (orderedIds == null || orderedIds.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Reorder: expected {expectedCount} ids for {table} under '{parentId}' but received {orderedIds?.Count ?? 0}.");
            }

            const int offset = 1000000;
            Sql.Execute(connection, transaction,
                $"UPDATE {table} SET ordinal = ordinal + {offset} WHERE {parentColumn} = @parent;",
                ("parent", parentId));

            for (var i = 0; i < orderedIds.Count; i++)
            {
                Sql.Execute(connection, transaction,
                    $"UPDATE {table} SET ordinal = @ordinal WHERE {idColumn} = @id AND {parentColumn} = @parent;",
                    ("ordinal", i), ("id", orderedIds[i]), ("parent", parentId));
            }
        }
    }
}
