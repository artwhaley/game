using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Narrow v2 repository: Phase graph nodes/edges. Whole-graph replace is a
    /// transaction: existing graph rows cascade away (subtype rows, output
    /// sockets, edges) and the new graph is written with stable IDs preserved.
    /// No generic graph abstraction — this owns exactly the phase low-level
    /// graph and its domain rules (singular Entry, typed nodes, ordered exits).
    /// </summary>
    public static class PhaseGraphRepository
    {
        public static void ReplaceGraph(DbConnection connection, string phaseId, PhaseGraphDefinition graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "DELETE FROM phase_graph_node WHERE phase_id = @phase;", ("phase", phaseId));

                    foreach (var node in graph.Nodes)
                    {
                        DatabaseInitializer.WritePhaseNode(connection, transaction, phaseId, node);
                    }

                    foreach (var edge in graph.Edges)
                    {
                        RequireId(edge?.Id, $"Edge on phase '{phaseId}'");
                        Sql.Execute(connection, transaction,
                            "INSERT INTO phase_graph_edge (id, phase_id, source_port_id, target_node_id) " +
                            "VALUES (@id, @phase, @source, @target);",
                            ("id", edge.Id), ("phase", phaseId),
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

        public static void RemoveNode(DbConnection connection, string phaseId, string nodeId)
        {
            Sql.Execute(connection, null,
                "DELETE FROM phase_graph_node WHERE id = @node AND phase_id = @phase;",
                ("node", nodeId), ("phase", phaseId));
        }

        /// <summary>
        /// Writes one new phase node (row + subtype rows + output sockets) as an
        /// atomic unit, reusing the shared initializer writer. Node ids are
        /// authored by the caller and must be unique within the phase.
        /// </summary>
        public static void AddNode(DbConnection connection, string phaseId, PhaseGraphNodeDefinition node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    DatabaseInitializer.WritePhaseNode(connection, transaction, phaseId, node);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Deletes every edge leaving the given output socket (disconnect persistence).</summary>
        public static void RemoveEdgesFromSource(DbConnection connection, string sourceOutputId)
        {
            if (string.IsNullOrEmpty(sourceOutputId)) return;
            Sql.Execute(connection, null,
                "DELETE FROM phase_graph_edge WHERE source_port_id = @source;",
                ("source", sourceOutputId));
        }

        /// <summary>Updates a VariableCheck node's comparison fields in place.</summary>
        public static void UpdateVariableCheck(DbConnection connection, string nodeId,
            VariableSourceKind sourceKind, string variableKey, VariableCompareOperator op, float compareValue)
        {
            Sql.Execute(connection, null,
                "UPDATE phase_node_variable_check SET source_kind = @kind, variable_key = @key, " +
                "compare_operator = @op, compare_value = @value WHERE node_id = @node;",
                ("kind", DatabaseInitializer.SourceKindName(sourceKind)),
                ("key", string.IsNullOrEmpty(variableKey) ? DBNull.Value : (object)variableKey),
                ("op", DatabaseInitializer.OperatorName(op)),
                ("value", (double)compareValue),
                ("node", nodeId));
        }

        public static void AddEdge(DbConnection connection, string phaseId, GraphEdgeDefinition edge)
        {
            if (edge == null) throw new ArgumentNullException(nameof(edge));
            RequireId(edge.Id, "Edge id");
            Sql.Execute(connection, null,
                "INSERT INTO phase_graph_edge (id, phase_id, source_port_id, target_node_id) " +
                "VALUES (@id, @phase, @source, @target);",
                ("id", edge.Id), ("phase", phaseId),
                ("source", edge.SourceOutputId), ("target", edge.TargetNodeId));
        }

        public static void RemoveEdge(DbConnection connection, string edgeId)
        {
            Sql.Execute(connection, null, "DELETE FROM phase_graph_edge WHERE id = @id;", ("id", edgeId));
        }

        private static void RequireId(string id, string what)
        {
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException(what + " has an empty id.");
        }
    }

    /// <summary>
    /// Narrow v2 repository: Session graph nodes/edges. Same replace semantics
    /// as PhaseGraphRepository; session sockets additionally carry phase-exit
    /// projections and session-goto instance mappings (discriminator-validated
    /// by the shared initializer writer).
    /// </summary>
    public static class SessionGraphRepository
    {
        public static void ReplaceGraph(DbConnection connection, string sessionId, SessionGraphDefinition graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "DELETE FROM session_graph_node WHERE session_id = @session;", ("session", sessionId));

                    foreach (var node in graph.Nodes)
                    {
                        DatabaseInitializer.WriteSessionNode(connection, transaction, sessionId, node);
                    }

                    foreach (var edge in graph.Edges)
                    {
                        RequireId(edge?.Id, $"Edge on session '{sessionId}'");
                        Sql.Execute(connection, transaction,
                            "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) " +
                            "VALUES (@id, @session, @source, @target);",
                            ("id", edge.Id), ("session", sessionId),
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

        public static void RemoveNode(DbConnection connection, string sessionId, string nodeId)
        {
            Sql.Execute(connection, null,
                "DELETE FROM session_graph_node WHERE id = @node AND session_id = @session;",
                ("node", nodeId), ("session", sessionId));
        }

        /// <summary>
        /// Writes one new session node (row + subtype rows + output sockets) as
        /// an atomic unit, reusing the shared initializer writer. Node ids are
        /// authored by the caller and must be unique within the session.
        /// </summary>
        public static void AddNode(DbConnection connection, string sessionId, SessionGraphNodeDefinition node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    DatabaseInitializer.WriteSessionNode(connection, transaction, sessionId, node);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Deletes every edge leaving the given output socket (disconnect persistence).</summary>
        public static void RemoveEdgesFromSource(DbConnection connection, string sourceOutputId)
        {
            if (string.IsNullOrEmpty(sourceOutputId)) return;
            Sql.Execute(connection, null,
                "DELETE FROM session_graph_edge WHERE source_port_id = @source;",
                ("source", sourceOutputId));
        }

        public static void AddEdge(DbConnection connection, string sessionId, GraphEdgeDefinition edge)
        {
            if (edge == null) throw new ArgumentNullException(nameof(edge));
            RequireId(edge.Id, "Edge id");
            Sql.Execute(connection, null,
                "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) " +
                "VALUES (@id, @session, @source, @target);",
                ("id", edge.Id), ("session", sessionId),
                ("source", edge.SourceOutputId), ("target", edge.TargetNodeId));
        }

        public static void RemoveEdge(DbConnection connection, string edgeId)
        {
            Sql.Execute(connection, null, "DELETE FROM session_graph_edge WHERE id = @id;", ("id", edgeId));
        }

        private static void RequireId(string id, string what)
        {
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException(what + " has an empty id.");
        }
    }
}
