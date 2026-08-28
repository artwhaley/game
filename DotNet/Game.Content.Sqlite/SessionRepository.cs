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

        /// <summary>Creates a new authoring session with its singular Start node and initial layout atomically.</summary>
        public static void CreateWithStart(DbConnection connection, string id, string title, string sessionTypeId)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Session id required.", nameof(id));
            if (string.IsNullOrEmpty(sessionTypeId)) throw new ArgumentException("Session type id required.", nameof(sessionTypeId));
            var nodeId = "sn-" + id + "-start";
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session (id, title, session_type_id) VALUES (@id, @title, @type);",
                        ("id", id), ("title", (object)title ?? DBNull.Value), ("type", sessionTypeId));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session_graph_node (id, session_id, node_type) VALUES (@node, @session, 'Start');",
                        ("node", nodeId), ("session", id));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) " +
                        "VALUES (@output, @node, 'normal', 0, NULL, NULL, NULL);",
                        ("output", nodeId + "-out"), ("node", nodeId));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO wpf_session_node_layout (session_id, node_id, x, y) VALUES (@session, @node, 0, 0);",
                        ("session", id), ("node", nodeId));
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
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
