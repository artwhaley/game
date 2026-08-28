using System;
using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Narrow authoring repository: Phase metadata rows (create / rename /
    /// delete / include-exclude tags) plus a usage-count read (distinct
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

        /// <summary>Deletes the phase; throws SqliteException when a session still references it (RESTRICT).</summary>
        public static void Delete(DbConnection connection, string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Phase id required.", nameof(id));
            Sql.Execute(connection, null, "DELETE FROM phase WHERE id = @id;", ("id", id));
        }

        public static void ReplaceRequiredTags(DbConnection connection, string phaseId, IReadOnlyList<string> tags)
        {
            ReplaceTags(connection, "phase_required_tag", phaseId, tags);
        }

        public static void ReplaceExcludedTags(DbConnection connection, string phaseId, IReadOnlyList<string> tags)
        {
            ReplaceTags(connection, "phase_excluded_tag", phaseId, tags);
        }

        public static void ReplaceAllTags(DbConnection connection, string phaseId,
            IReadOnlyList<string> includeTags, IReadOnlyList<string> excludeTags)
        {
            if (includeTags == null) throw new ArgumentNullException(nameof(includeTags));
            if (excludeTags == null) throw new ArgumentNullException(nameof(excludeTags));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ReplaceTags(connection, transaction, "phase_required_tag", phaseId, includeTags);
                    ReplaceTags(connection, transaction, "phase_excluded_tag", phaseId, excludeTags);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void ReplaceTags(DbConnection connection, string table, string phaseId, IReadOnlyList<string> tags)
        {
            if (string.IsNullOrEmpty(phaseId)) throw new ArgumentException("Phase id required.", nameof(phaseId));
            if (tags == null) throw new ArgumentNullException(nameof(tags));

            ReplaceTags(connection, null, table, phaseId, tags);
        }

        private static void ReplaceTags(DbConnection connection, DbTransaction transaction, string table, string phaseId, IReadOnlyList<string> tags)
        {
            if (string.IsNullOrEmpty(phaseId)) throw new ArgumentException("Phase id required.", nameof(phaseId));
            if (tags == null) throw new ArgumentNullException(nameof(tags));

            Sql.Execute(connection, transaction,
                $"DELETE FROM {table} WHERE phase_id = @phase;", ("phase", phaseId));
            Sql.EnsureTags(connection, transaction, tags);
            for (var i = 0; i < tags.Count; i++)
            {
                if (string.IsNullOrEmpty(tags[i]))
                {
                    throw new InvalidOperationException($"{table}: null/empty tag at index {i} for '{phaseId}'.");
                }
                Sql.Execute(connection, transaction,
                    $"INSERT INTO {table} (phase_id, tag_id, ordinal) VALUES (@phase, @tag, @ordinal);",
                    ("phase", phaseId), ("tag", tags[i]), ("ordinal", i));
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
