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
        public static void WriteClonedPhase(DbConnection connection, PhaseDefinition phase,
            IDictionary<string, string> edgeIdMap = null, IDictionary<string, string> portalIdMap = null)
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
                    CopyPortalRows(connection, transaction, "phase", phase.Id, edgeIdMap, portalIdMap);

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void WriteClonedSession(DbConnection connection, SessionDefinition session,
            IDictionary<string, string> edgeIdMap = null, IDictionary<string, string> portalIdMap = null)
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
                    CopyPortalRows(connection, transaction, "session", session.Id, edgeIdMap, portalIdMap);

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        internal static void CopyPortalRows(DbConnection connection, DbTransaction transaction,
            string graphKind, string graphId, IDictionary<string, string> edgeIdMap,
            IDictionary<string, string> portalIdMap = null)
        {
            if (edgeIdMap == null || edgeIdMap.Count == 0) return;
            var sourceTable = graphKind == "session"
                ? "wpf_session_edge_portal_pair" : "wpf_phase_edge_portal_pair";
            var ownerColumn = graphKind == "session" ? "session_id" : "phase_id";
            foreach (var mapping in edgeIdMap)
            {
                GraphPortalPairDefinition source = null;
                Sql.QueryAll(connection,
                    $"SELECT id, {ownerColumn}, edge_id, label, color_slot, source_x, source_y, target_x, target_y " +
                    $"FROM {sourceTable} WHERE edge_id = @edge;",
                    reader => source = new GraphPortalPairDefinition
                    {
                        Id = reader.GetString(0), GraphId = reader.GetString(1), EdgeId = reader.GetString(2),
                        Label = reader.GetString(3), ColorSlot = reader.GetInt32(4), SourceX = reader.GetDouble(5),
                        SourceY = reader.GetDouble(6), TargetX = reader.GetDouble(7), TargetY = reader.GetDouble(8),
                    }, ("edge", mapping.Key));
                if (source == null) continue;
                var cloneId = portalIdMap != null && portalIdMap.TryGetValue(source.Id, out var mappedId)
                    ? mappedId : StableIds.New();
                if (portalIdMap != null) portalIdMap[source.Id] = cloneId;
                var clone = CreateClonePortal(source, graphId, mapping.Value, cloneId);
                var cloneTable = sourceTable;
                Sql.Execute(connection, transaction,
                    $"INSERT INTO {cloneTable} (id, {ownerColumn}, edge_id, label, color_slot, source_x, source_y, target_x, target_y) " +
                    "VALUES (@id, @owner, @edge, @label, @color, @sx, @sy, @tx, @ty);",
                    ("id", clone.Id), ("owner", clone.GraphId), ("edge", clone.EdgeId), ("label", clone.Label),
                    ("color", clone.ColorSlot), ("sx", clone.SourceX), ("sy", clone.SourceY),
                    ("tx", clone.TargetX), ("ty", clone.TargetY));
            }
        }

        private static GraphPortalPairDefinition CreateClonePortal(GraphPortalPairDefinition source,
            string graphId, string edgeId, string cloneId)
        {
            return new GraphPortalPairDefinition
            {
                Id = cloneId, GraphId = graphId, EdgeId = edgeId, Label = source.Label,
                ColorSlot = source.ColorSlot, SourceX = source.SourceX, SourceY = source.SourceY,
                TargetX = source.TargetX, TargetY = source.TargetY,
            };
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
