using System;
using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Narrow authoring repository: Phase metadata rows (create / rename /
    /// delete / card query tags) plus a usage-count read (distinct
    /// referencing sessions and total placements) for the Phase header.
    /// Deleting a phase cascades its low-level graph; the schema's RESTRICT
    /// foreign key blocks deleting a phase that a session still references.
    /// </summary>
    public static class PhaseRepository
    {
        public static void Create(DbConnection connection, string id, string title)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Phase id required.", nameof(id));
            Sql.Execute(connection, null,
                "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, @title, 0, 0);",
                ("id", id), ("title", (object)title ?? DBNull.Value));
        }
        /// <summary>Creates a new authoring phase with its singular Entry node and initial layout atomically.</summary>
        public static void CreateWithEntry(DbConnection connection, string id, string title)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Phase id required.", nameof(id));
            var nodeId = "pn-" + id + "-entry";
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, @title, 0, 0);",
                        ("id", id), ("title", (object)title ?? DBNull.Value));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO phase_graph_node (id, phase_id, node_type) VALUES (@node, @phase, 'Entry');",
                        ("node", nodeId), ("phase", id));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO phase_node_output (id, node_id, port_kind, ordinal, label) VALUES (@output, @node, 'normal', 0, NULL);",
                        ("output", nodeId + "-out"), ("node", nodeId));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO wpf_phase_node_layout (phase_id, node_id, x, y) VALUES (@phase, @node, 0, 0);",
                        ("phase", id), ("node", nodeId));
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
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Phase id required.", nameof(id));
            Sql.Execute(connection, null,
                "UPDATE phase SET title = @title WHERE id = @id;",
                ("title", (object)title ?? DBNull.Value), ("id", id));
        }

        /// <summary>
        /// Deletes the phase and its owned graph. PhaseGoto rows must be
        /// detached from the phase's exits first because their RESTRICT FK is
        /// checked before SQLite processes the owning graph's cascades.
        /// Session placements remain protected by their phase RESTRICT FK.
        /// </summary>
        public static void Delete(DbConnection connection, string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Phase id required.", nameof(id));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var ownedSequences = new List<string>();
                    Sql.QueryAll(connection, transaction,
                        "SELECT pna.action_sequence_id FROM phase_node_action pna " +
                        "JOIN phase_graph_node n ON n.id = pna.node_id WHERE n.phase_id = @id " +
                        "UNION SELECT pdo.action_sequence_id FROM phase_decision_option pdo " +
                        "JOIN phase_graph_node n ON n.id = pdo.node_id WHERE n.phase_id = @id;",
                        reader => ownedSequences.Add(reader.GetString(0)), ("id", id));
                    foreach (var sequenceId in ownedSequences)
                    {
                        ActionSequenceWriter.ClearContents(connection, transaction, sequenceId);
                        ActionSequenceWriter.Delete(connection, transaction, sequenceId);
                    }

                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_phase_goto SET phase_exit_id = NULL " +
                        "WHERE phase_exit_id IN (SELECT id FROM phase_exit WHERE phase_id = @id);",
                        ("id", id));
                    Sql.Execute(connection, transaction,
                        "DELETE FROM phase WHERE id = @id;", ("id", id));
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Replaces the Phase's include-only Card tag query (v5). Empty ALL =
        /// no restriction; empty ANY = no restriction.
        /// </summary>
        public static void ReplaceCardQuery(DbConnection connection, string phaseId,
            IReadOnlyList<string> allTagIds, IReadOnlyList<string> anyTagIds)
        {
            if (string.IsNullOrEmpty(phaseId)) throw new ArgumentException("Phase id required.", nameof(phaseId));
            if (allTagIds == null) throw new ArgumentNullException(nameof(allTagIds));
            if (anyTagIds == null) throw new ArgumentNullException(nameof(anyTagIds));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ReplaceQueryTags(connection, transaction, "phase_card_all_tag", phaseId, allTagIds);
                    ReplaceQueryTags(connection, transaction, "phase_card_any_tag", phaseId, anyTagIds);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void ReplaceQueryTags(DbConnection connection, DbTransaction transaction, string table, string phaseId, IReadOnlyList<string> tagIds)
        {
            Sql.Execute(connection, transaction,
                $"DELETE FROM {table} WHERE phase_id = @phase;", ("phase", phaseId));
            for (var i = 0; i < tagIds.Count; i++)
            {
                if (string.IsNullOrEmpty(tagIds[i]))
                {
                    throw new InvalidOperationException($"{table}: null/empty tag at index {i} for '{phaseId}'.");
                }
                Sql.Execute(connection, transaction,
                    $"INSERT INTO {table} (phase_id, tag_id, ordinal) VALUES (@phase, @tag, @ordinal);",
                    ("phase", phaseId), ("tag", tagIds[i]), ("ordinal", i));
            }
        }

        /// <summary>Returns (distinctSessions, totalPlacements) referencing the phase.</summary>
        public static (int Sessions, int Placements) Usage(DbConnection connection, string phaseId)
        {
            if (string.IsNullOrEmpty(phaseId)) return (0, 0);

            var sessions = 0;
            var placements = 0;
            Sql.QueryAll(connection,
                "SELECT COUNT(DISTINCT n.session_id), COUNT(*) FROM session_node_phase p " +
                "JOIN session_graph_node n ON n.id = p.node_id WHERE p.phase_id = @phase;",
                reader =>
                {
                    sessions = Convert.ToInt32(reader.GetValue(0));
                    placements = Convert.ToInt32(reader.GetValue(1));
                },
                ("phase", phaseId));
            return (sessions, placements);
        }

        /// <summary>Titles of the sessions that reference the phase (for the "Show Sessions" readout).</summary>
        public static List<string> ReferencingSessionTitles(DbConnection connection, string phaseId)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(phaseId)) return result;
            Sql.QueryAll(connection,
                "SELECT DISTINCT s.title FROM session_node_phase p " +
                "JOIN session_graph_node n ON n.id = p.node_id " +
                "JOIN session s ON s.id = n.session_id " +
                "WHERE p.phase_id = @phase ORDER BY s.title;",
                reader => result.Add(reader.GetString(0)),
                ("phase", phaseId));
            return result;
        }
    }
}
