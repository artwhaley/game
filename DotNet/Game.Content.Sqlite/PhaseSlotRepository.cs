using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Granular PhaseSlot (and candidate) persistence for the future WPF
    /// authoring UI. Reorders never violate the unique (parent, ordinal)
    /// constraint: they shift then stamp inside one transaction.
    /// </summary>
    public static class PhaseSlotRepository
    {
        /// <summary>Appends a slot at the end of the session's ordered slots.</summary>
        public static void Create(DbConnection connection, string sessionId, string slotId, string title)
        {
            if (string.IsNullOrEmpty(slotId)) throw new ArgumentException("Slot id must not be empty.", nameof(slotId));
            ConnectionInitializer.Initialize(connection);

            using (var transaction = connection.BeginTransaction())
            {
                var next = NextOrdinal(connection, transaction, "phase_slot", "session_id", sessionId);
                Sql.Execute(connection, transaction,
                    "INSERT INTO phase_slot (id, session_id, ordinal, title) VALUES (@id, @session, @ordinal, @title);",
                    ("id", slotId), ("session", sessionId), ("ordinal", next), ("title", title));
                transaction.Commit();
            }
        }

        public static void UpdateTitle(DbConnection connection, string slotId, string title)
        {
            ConnectionInitializer.Initialize(connection);
            Sql.Execute(connection, null,
                "UPDATE phase_slot SET title = @title WHERE id = @id;",
                ("title", title), ("id", slotId));
        }

        public static void Delete(DbConnection connection, string slotId)
        {
            ConnectionInitializer.Initialize(connection);
            Sql.Execute(connection, null,
                "DELETE FROM phase_slot WHERE id = @id;",
                ("id", slotId));
        }

        /// <summary>Full reorder of a session's slots; the list must contain every slot id.</summary>
        public static void Reorder(DbConnection connection, string sessionId, IReadOnlyList<string> slotIds)
        {
            ConnectionInitializer.Initialize(connection);
            using (var transaction = connection.BeginTransaction())
            {
                var count = Count(connection, transaction, "phase_slot", "session_id", sessionId);
                Sql.Reorder(connection, transaction, "phase_slot", "session_id", sessionId, "id", slotIds, count);
                transaction.Commit();
            }
        }

        public static List<PhaseSlotCandidateDefinition> ListCandidates(DbConnection connection, string slotId)
        {
            ConnectionInitializer.Initialize(connection);

            var candidates = new List<PhaseSlotCandidateDefinition>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, phase_id FROM phase_slot_candidate WHERE phase_slot_id = @id ORDER BY ordinal;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = slotId;
                command.Parameters.Add(parameter);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        candidates.Add(new PhaseSlotCandidateDefinition { Id = reader.GetString(0), PhaseId = reader.GetString(1) });
                    }
                }
            }
            return candidates;
        }

        /// <summary>Appends a candidate at the end of the slot's ordered candidates.</summary>
        public static void AddCandidate(DbConnection connection, string slotId, string candidateId, string phaseId)
        {
            if (string.IsNullOrEmpty(candidateId)) throw new ArgumentException("Candidate id must not be empty.", nameof(candidateId));
            ConnectionInitializer.Initialize(connection);

            using (var transaction = connection.BeginTransaction())
            {
                var next = NextOrdinal(connection, transaction, "phase_slot_candidate", "phase_slot_id", slotId);
                Sql.Execute(connection, transaction,
                    "INSERT INTO phase_slot_candidate (id, phase_slot_id, ordinal, phase_id) VALUES (@id, @slot, @ordinal, @phase);",
                    ("id", candidateId), ("slot", slotId), ("ordinal", next), ("phase", phaseId));
                transaction.Commit();
            }
        }

        public static void RemoveCandidate(DbConnection connection, string candidateId)
        {
            ConnectionInitializer.Initialize(connection);
            Sql.Execute(connection, null,
                "DELETE FROM phase_slot_candidate WHERE id = @id;",
                ("id", candidateId));
        }

        /// <summary>Full reorder of a slot's candidates; the list must contain every candidate id.</summary>
        public static void ReorderCandidates(DbConnection connection, string slotId, IReadOnlyList<string> candidateIds)
        {
            ConnectionInitializer.Initialize(connection);
            using (var transaction = connection.BeginTransaction())
            {
                var count = Count(connection, transaction, "phase_slot_candidate", "phase_slot_id", slotId);
                Sql.Reorder(connection, transaction, "phase_slot_candidate", "phase_slot_id", slotId, "id", candidateIds, count);
                transaction.Commit();
            }
        }

        private static int NextOrdinal(DbConnection connection, DbTransaction transaction, string table, string parentColumn, string parentId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"SELECT COALESCE(MAX(ordinal) + 1, 0) FROM {table} WHERE {parentColumn} = @parent;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@parent";
                parameter.Value = parentId;
                command.Parameters.Add(parameter);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private static int Count(DbConnection connection, DbTransaction transaction, string table, string parentColumn, string parentId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {parentColumn} = @parent;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@parent";
                parameter.Value = parentId;
                command.Parameters.Add(parameter);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
    }
}
