using System;
using System.Collections.Generic;
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

                    WriteCardQuery(connection, transaction, phase);

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

                    var weighting = session.CardWeighting ?? new SessionCardWeightingDefinition();
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session_card_weighting " +
                        "(session_id, love_base, love_happiness_gain, like_base, like_happiness_gain, torture_base, torture_unhappiness_gain) " +
                        "VALUES (@id, @lb, @lg, @kb, @kg, @tb, @tg);",
                        ("id", session.Id),
                        ("lb", (double)weighting.LoveBase), ("lg", (double)weighting.LoveHappinessGain),
                        ("kb", (double)weighting.LikeBase), ("kg", (double)weighting.LikeHappinessGain),
                        ("tb", (double)weighting.TortureBase), ("tg", (double)weighting.TortureUnhappinessGain));

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

        private static void WriteCardQuery(DbConnection connection, DbTransaction transaction, PhaseDefinition phase)
        {
            WriteQueryTable(connection, transaction, "phase_card_all_tag", phase.Id, phase.MustHaveAllCardTags);
            WriteQueryTable(connection, transaction, "phase_card_any_tag", phase.Id, phase.MustHaveAnyCardTags);
        }

        private static void WriteQueryTable(DbConnection connection, DbTransaction transaction, string table, string phaseId, IReadOnlyList<string> tagIds)
        {
            for (var i = 0; i < tagIds.Count; i++)
            {
                if (string.IsNullOrEmpty(tagIds[i])) continue;
                Sql.Execute(connection, transaction,
                    $"INSERT INTO {table} (phase_id, tag_id, ordinal) VALUES (@parent, @tag, @ordinal);",
                    ("parent", phaseId), ("tag", tagIds[i]), ("ordinal", i));
            }
        }
    }
}
