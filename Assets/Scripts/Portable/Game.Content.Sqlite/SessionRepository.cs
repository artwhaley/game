using System;
using System.Data.Common;
using TruthCardGame.Content;

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

        /// <summary>Replaces the Session's Card preference weighting (v5); nonnegative values only.</summary>
        public static void ReplaceCardWeighting(DbConnection connection, string sessionId, SessionCardWeightingDefinition weighting)
        {
            if (string.IsNullOrEmpty(sessionId)) throw new ArgumentException("Session id required.", nameof(sessionId));
            if (weighting == null) throw new ArgumentNullException(nameof(weighting));
            ValidateNonnegative(weighting, sessionId);

            Sql.Execute(connection, null,
                "INSERT INTO session_card_weighting " +
                "(session_id, love_base, love_happiness_gain, like_base, like_happiness_gain, torture_base, torture_unhappiness_gain) " +
                "VALUES (@id, @lb, @lg, @kb, @kg, @tb, @tg) " +
                "ON CONFLICT(session_id) DO UPDATE SET love_base = @lb, love_happiness_gain = @lg, like_base = @kb, " +
                "like_happiness_gain = @kg, torture_base = @tb, torture_unhappiness_gain = @tg;",
                ("id", sessionId),
                ("lb", (double)weighting.LoveBase), ("lg", (double)weighting.LoveHappinessGain),
                ("kb", (double)weighting.LikeBase), ("kg", (double)weighting.LikeHappinessGain),
                ("tb", (double)weighting.TortureBase), ("tg", (double)weighting.TortureUnhappinessGain));
        }

        private static void ValidateNonnegative(SessionCardWeightingDefinition weighting, string sessionId)
        {
            if (weighting.LoveBase < 0f || weighting.LoveHappinessGain < 0f ||
                weighting.LikeBase < 0f || weighting.LikeHappinessGain < 0f ||
                weighting.TortureBase < 0f || weighting.TortureUnhappinessGain < 0f)
            {
                throw new InvalidOperationException(
                    $"Session '{sessionId}': Card weighting values must be nonnegative.");
            }
        }

        public static void Delete(DbConnection connection, string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Session id required.", nameof(id));
            Sql.Execute(connection, null, "DELETE FROM session WHERE id = @id;", ("id", id));
        }
    }
}
