using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Granular Session persistence for the future WPF authoring UI. Each
    /// logical multi-row edit is one transaction; relationship rows are
    /// replaced transactionally but core rows are updated in place. Host
    /// extension rows are never touched.
    /// </summary>
    public static class SessionRepository
    {
        public static List<SessionDefinition> List(DbConnection connection)
        {
            ConnectionInitializer.Initialize(connection);

            var sessions = new List<SessionDefinition>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title FROM session ORDER BY id;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sessions.Add(new SessionDefinition { Id = reader.GetString(0), Title = reader.GetString(1) });
                    }
                }
            }

            var byId = new Dictionary<string, SessionDefinition>();
            foreach (var session in sessions) byId[session.Id] = session;

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT st.session_id, t.name FROM session_tag st " +
                    "JOIN tag t ON t.id = st.tag_id ORDER BY st.session_id, st.ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byId[reader.GetString(0)].Tags.Add(reader.GetString(1));
                    }
                }
            }

            return sessions;
        }

        public static SessionDefinition Get(DbConnection connection, string sessionId)
        {
            foreach (var session in List(connection))
            {
                if (session.Id == sessionId) return session;
            }
            throw new InvalidOperationException($"SessionRepository: no session with id '{sessionId}'.");
        }

        public static void Create(DbConnection connection, string id, string title, IReadOnlyList<string> tags)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Session id must not be empty.", nameof(id));
            ConnectionInitializer.Initialize(connection);

            using (var transaction = connection.BeginTransaction())
            {
                Sql.EnsureTags(connection, transaction, tags);
                Sql.Execute(connection, transaction,
                    "INSERT INTO session (id, title) VALUES (@id, @title);",
                    ("id", id), ("title", title));
                InsertTags(connection, transaction, id, tags);
                transaction.Commit();
            }
        }

        public static void UpdateTitle(DbConnection connection, string sessionId, string title)
        {
            ConnectionInitializer.Initialize(connection);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE session SET title = @title WHERE id = @id;";
                var p1 = command.CreateParameter();
                p1.ParameterName = "@title";
                p1.Value = title;
                command.Parameters.Add(p1);
                var p2 = command.CreateParameter();
                p2.ParameterName = "@id";
                p2.Value = sessionId;
                command.Parameters.Add(p2);
                command.ExecuteNonQuery();
            }
        }

        public static void ReplaceTags(DbConnection connection, string sessionId, IReadOnlyList<string> tags)
        {
            ConnectionInitializer.Initialize(connection);
            using (var transaction = connection.BeginTransaction())
            {
                Sql.Execute(connection, transaction,
                    "DELETE FROM session_tag WHERE session_id = @id;",
                    ("id", sessionId));
                Sql.EnsureTags(connection, transaction, tags);
                InsertTags(connection, transaction, sessionId, tags);
                transaction.Commit();
            }
        }

        public static void Delete(DbConnection connection, string sessionId)
        {
            ConnectionInitializer.Initialize(connection);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM session WHERE id = @id;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = sessionId;
                command.Parameters.Add(parameter);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Ordered PhaseSlots (with their candidates) of one session.</summary>
        public static List<PhaseSlotDefinition> ListPhaseSlots(DbConnection connection, string sessionId)
        {
            ConnectionInitializer.Initialize(connection);

            var slots = new List<PhaseSlotDefinition>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title FROM phase_slot WHERE session_id = @id ORDER BY ordinal;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = sessionId;
                command.Parameters.Add(parameter);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        slots.Add(new PhaseSlotDefinition { Id = reader.GetString(0), Title = reader.GetString(1) });
                    }
                }
            }

            var byId = new Dictionary<string, PhaseSlotDefinition>();
            foreach (var slot in slots) byId[slot.Id] = slot;

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT phase_slot_id, id, phase_id FROM phase_slot_candidate " +
                    "WHERE phase_slot_id IN (SELECT id FROM phase_slot WHERE session_id = @id) " +
                    "ORDER BY phase_slot_id, ordinal;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = sessionId;
                command.Parameters.Add(parameter);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byId[reader.GetString(0)].Candidates.Add(
                            new PhaseSlotCandidateDefinition { Id = reader.GetString(1), PhaseId = reader.GetString(2) });
                    }
                }
            }

            return slots;
        }

        private static void InsertTags(DbConnection connection, DbTransaction transaction, string sessionId, IReadOnlyList<string> tags)
        {
            if (tags == null) return;
            for (var i = 0; i < tags.Count; i++)
            {
                if (string.IsNullOrEmpty(tags[i])) continue;
                Sql.Execute(connection, transaction,
                    "INSERT INTO session_tag (session_id, tag_id, ordinal) VALUES (@session, @tag, @ordinal);",
                    ("session", sessionId), ("tag", tags[i]), ("ordinal", i));
            }
        }
    }
}
