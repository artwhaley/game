using System;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Narrow authoring repository: Session metadata rows (create / rename /
    /// delete / session-type change). Graph topology lives in
    /// SessionGraphRepository; node coordinates in AuthoringLayoutRepository.
    /// Deleting a session cascades its graph nodes, outputs and edges via the
    /// schema's ON DELETE CASCADE chain.
    /// </summary>
    public static class SessionRepository
    {
        public static void Create(DbConnection connection, string id, string title, string sessionTypeId)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Session id required.", nameof(id));
            if (string.IsNullOrEmpty(sessionTypeId)) throw new ArgumentException("Session type id required.", nameof(sessionTypeId));

            Sql.Execute(connection, null,
                "INSERT INTO session (id, title, session_type_id) VALUES (@id, @title, @type);",
                ("id", id), ("title", (object)title ?? DBNull.Value), ("type", sessionTypeId));
        }

        public static void Rename(DbConnection connection, string id, string title)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Session id required.", nameof(id));
            Sql.Execute(connection, null,
                "UPDATE session SET title = @title WHERE id = @id;",
                ("title", (object)title ?? DBNull.Value), ("id", id));
        }

        public static void SetSessionType(DbConnection connection, string id, string sessionTypeId)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Session id required.", nameof(id));
            if (string.IsNullOrEmpty(sessionTypeId)) throw new ArgumentException("Session type id required.", nameof(sessionTypeId));
            Sql.Execute(connection, null,
                "UPDATE session SET session_type_id = @type WHERE id = @id;",
                ("type", sessionTypeId), ("id", id));
        }

        public static void Delete(DbConnection connection, string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Session id required.", nameof(id));
            Sql.Execute(connection, null, "DELETE FROM session WHERE id = @id;", ("id", id));
        }
    }
}
