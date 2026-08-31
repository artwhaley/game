using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 17 Make Unique: detaches ONE Session PhaseReference placement from
    /// its shared Phase by cloning the phase (new ids), remapping old exit ids to
    /// the clone's exit ids, re-pointing the placement's PhaseId, and updating the
    /// placement's projected output mappings — while preserving the placement's
    /// port ids and every existing Session edge. All in one transaction.
    /// </summary>
    public static class MakeUniqueRepository
    {
        /// <summary>Returns the new (cloned) phase id.</summary>
        public static string MakeUnique(
            DbConnection connection,
            PhaseDefinition sharedPhase,
            string placementNodeId,
            string newPhaseId,
            IDictionary<string, string> portalIdMap = null)
        {
            if (sharedPhase == null) throw new ArgumentNullException(nameof(sharedPhase));
            if (string.IsNullOrEmpty(placementNodeId)) throw new ArgumentException("Placement node id required.", nameof(placementNodeId));
            if (string.IsNullOrEmpty(newPhaseId)) throw new ArgumentException("New phase id required.", nameof(newPhaseId));

            var clone = ContentCloner.ClonePhase(sharedPhase, newPhaseId);

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // Clone the phase row, exits, tags and graph (new ids).
                    // ReuseWriter does its own transaction, so inline the phase
                    // writes here instead to stay inside THIS transaction.
                    WritePhaseInside(connection, transaction, clone.Phase);
                    ReuseWriter.CopyPortalRows(connection, transaction, "phase", newPhaseId, clone.EdgeIdMap, portalIdMap);

                    // Re-point the placement.
                    Sql.Execute(connection, transaction,
                        "UPDATE session_node_phase SET phase_id = @newPhase WHERE node_id = @node;",
                        ("newPhase", newPhaseId), ("node", placementNodeId));

                    // Remap the projected outputs on this placement, preserving the
                    // port row ids and their edges (identity of an edge is the port id).
                    foreach (var pair in clone.ExitIdMap)
                    {
                        Sql.Execute(connection, transaction,
                            "UPDATE session_node_output SET phase_exit_id = @newExit " +
                            "WHERE node_id = @node AND phase_exit_id = @oldExit;",
                            ("newExit", pair.Value), ("node", placementNodeId), ("oldExit", pair.Key));
                    }

                    transaction.Commit();
                    return newPhaseId;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void WritePhaseInside(DbConnection connection, DbTransaction transaction, PhaseDefinition phase)
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
        }
    }
}
