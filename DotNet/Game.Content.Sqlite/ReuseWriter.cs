using System;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 17: persists a deep-cloned Phase or Session (new ids throughout)
    /// into an EXISTING database in a single transaction. Reuses the shared
    /// node writers so clone persistence matches seed/replace semantics exactly.
    /// </summary>
    public static class ReuseWriter
    {
        public static void WriteClonedPhase(DbConnection connection, PhaseDefinition phase)
        {
            if (phase == null) throw new ArgumentNullException(nameof(phase));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, @title, 0, 0);",
                        ("id", phase.Id), ("title", phase.Title));

                    for (var i = 0; i < phase.Exits.Count; i++)
                    {
                        var exit = phase.Exits[i];
                        Sql.Execute(connection, transaction,
                            "INSERT INTO phase_exit (id, phase_id, ordinal, name) VALUES (@id, @phase, @ordinal, @name);",
                            ("id", exit.Id), ("phase", phase.Id), ("ordinal", i), ("name", exit.Name));
                    }

                    WriteTags(connection, transaction, "phase_required_tag", phase.Id, phase.MustIncludeTags);
                    WriteTags(connection, transaction, "phase_excluded_tag", phase.Id, phase.MustExcludeTags);

                    foreach (var node in phase.Graph.Nodes)
                    {
                        DatabaseInitializer.WritePhaseNode(connection, transaction, phase.Id, node);
                    }
                    foreach (var edge in phase.Graph.Edges)
                    {
                        Sql.Execute(connection, transaction,
                            "INSERT INTO phase_graph_edge (id, phase_id, source_port_id, target_node_id) " +
                            "VALUES (@id, @phase, @source, @target);",
                            ("id", edge.Id), ("phase", phase.Id),
                            ("source", edge.SourceOutputId), ("target", edge.TargetNodeId));
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void WriteClonedSession(DbConnection connection, SessionDefinition session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session (id, title, session_type_id) VALUES (@id, @title, @type);",
                        ("id", session.Id), ("title", session.Title), ("type", session.SessionTypeId));
                    WriteTags(connection, transaction, "session_tag", session.Id, session.Tags);

                    foreach (var node in session.Graph.Nodes)
                    {
                        DatabaseInitializer.WriteSessionNode(connection, transaction, session.Id, node);
                    }
                    foreach (var edge in session.Graph.Edges)
                    {
                        Sql.Execute(connection, transaction,
                            "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) " +
                            "VALUES (@id, @session, @source, @target);",
                            ("id", edge.Id), ("session", session.Id),
                            ("source", edge.SourceOutputId), ("target", edge.TargetNodeId));
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void WriteTags(DbConnection connection, DbTransaction transaction, string table, string parentId, System.Collections.Generic.IReadOnlyList<string> tagNames)
        {
            Sql.Execute(connection, transaction,
                $"DELETE FROM {table} WHERE {(table == "session_tag" ? "session_id" : "phase_id")} = @parent;",
                ("parent", parentId));
            Sql.EnsureTags(connection, transaction, tagNames);
            var parentColumn = table == "session_tag" ? "session_id" : "phase_id";
            for (var i = 0; i < tagNames.Count; i++)
            {
                if (string.IsNullOrEmpty(tagNames[i])) continue;
                Sql.Execute(connection, transaction,
                    $"INSERT INTO {table} ({parentColumn}, tag_id, ordinal) VALUES (@parent, @tag, @ordinal);",
                    ("parent", parentId), ("tag", tagNames[i]), ("ordinal", i));
            }
        }
    }
}
