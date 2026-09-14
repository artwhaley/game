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
        /// <remarks>
        /// Import/restore API only. Normal Workbench edits use AddNode, remove,
        /// and semantic commands so stable IDs and host extension rows are not
        /// replaced wholesale.
        /// </remarks>
        internal static void ReplaceGraph(DbConnection connection, string phaseId, PhaseGraphDefinition graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            var entryCount = 0;
            foreach (var node in graph.Nodes)
                if (node is PhaseEntryNodeDefinition) entryCount++;
            if (entryCount != 1)
                throw new InvalidOperationException($"Phase '{phaseId}' must have exactly one Entry node (found {entryCount}).");

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
            var nodeType = ReadNodeType(connection, "phase_graph_node", nodeId, phaseId);
            if (nodeType == "Entry")
                throw new InvalidOperationException("The Phase Entry node is singular and cannot be deleted.");
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

            if (node is PhaseEntryNodeDefinition)
            {
                var existing = Convert.ToInt32(SqlScalar(connection,
                    "SELECT COUNT(*) FROM phase_graph_node WHERE phase_id = @phase AND node_type = 'Entry';",
                    ("phase", phaseId)));
                if (existing > 0)
                    throw new InvalidOperationException("A phase may contain exactly one Entry node.");
            }

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

        private static string ReadNodeType(DbConnection connection, string table, string nodeId, string parentId)
        {
            return Convert.ToString(SqlScalar(connection,
                $"SELECT node_type FROM {table} WHERE id = @node AND phase_id = @phase;",
                ("node", nodeId), ("phase", parentId)));
        }

        private static object SqlScalar(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                foreach (var parameter in parameters)
                {
                    var dbParameter = command.CreateParameter();
                    dbParameter.ParameterName = parameter.Name;
                    dbParameter.Value = parameter.Value ?? DBNull.Value;
                    command.Parameters.Add(dbParameter);
                }
                return command.ExecuteScalar();
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

        /// <summary>
        /// The PhaseGoto instances inside an ActionNode's sequence, as (instanceId,
        /// exitId) pairs in sequence order. An ActionNode with no goto instances
        /// returns an empty list (the inline editor offers an add control).
        /// </summary>
        public static List<(string InstanceId, string ExitId)> ListPhaseGotoInstances(DbConnection connection, string nodeId)
        {
            var result = new List<(string, string)>();
            Sql.QueryAll(connection,
                "SELECT ai.id, g.phase_exit_id FROM action_instance ai " +
                "JOIN phase_node_action pna ON pna.action_sequence_id = ai.action_sequence_id " +
                "JOIN action_instance_phase_goto g ON g.action_instance_id = ai.id " +
                "WHERE pna.node_id = @node ORDER BY ai.ordinal;",
                reader => result.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1))),
                ("node", nodeId));
            return result;
        }

        /// <summary>
        /// Appends one blocking PhaseGoto instance to the ActionNode's sequence,
        /// referencing the given exit. Instance id is {nodeId}-goto-{n} and stable.
        /// </summary>
        public static void AddPhaseGoto(DbConnection connection, string nodeId, string exitId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException("Node id required.", nameof(nodeId));
            var sequenceId = GetActionSequenceId(connection, nodeId);
            AddPhaseGotoToSequence(connection, sequenceId, nodeId + "-", exitId);
        }

        /// <summary>
        /// Appends one blocking PhaseGoto instance to a decision-option sequence
        /// (PhaseDecision option rows use the same Phase Action palette).
        /// </summary>
        public static void AddPhaseGotoToOption(DbConnection connection, string optionId, string exitId)
        {
            if (string.IsNullOrEmpty(optionId)) throw new ArgumentException("Option id required.", nameof(optionId));

            var sequenceId = "";
            Sql.QueryAll(connection,
                "SELECT action_sequence_id FROM phase_decision_option WHERE id = @option;",
                reader => sequenceId = reader.GetString(0),
                ("option", optionId));
            if (string.IsNullOrEmpty(sequenceId))
            {
                throw new InvalidOperationException($"Phase decision option '{optionId}' has no sequence.");
            }
            AddPhaseGotoToSequence(connection, sequenceId, optionId + "-", exitId);
        }

        /// <summary>Shared PhaseGoto append: one instance row + subtype row.</summary>
        private static void AddPhaseGotoToSequence(DbConnection connection, string sequenceId, string idPrefix, string exitId)
        {
            var nextOrdinal = NextInstanceOrdinal(connection, sequenceId);
            var instanceId = idPrefix + "goto-" + nextOrdinal;
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                        "VALUES (@id, @seq, @ordinal, @type, 1);",
                        ("id", instanceId), ("seq", sequenceId), ("ordinal", nextOrdinal),
                        ("type", ActionType.PhaseGotoV2));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_phase_goto (action_instance_id, phase_exit_id) VALUES (@i, @exit);",
                        ("i", instanceId),
                        ("exit", string.IsNullOrEmpty(exitId) ? DBNull.Value : (object)exitId));
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Re-points an existing PhaseGoto instance at a different exit (inline ComboBox).</summary>
        public static void SetPhaseGotoExit(DbConnection connection, string instanceId, string exitId)
        {
            Sql.Execute(connection, null,
                "UPDATE action_instance_phase_goto SET phase_exit_id = @exit WHERE action_instance_id = @i;",
                ("exit", string.IsNullOrEmpty(exitId) ? DBNull.Value : (object)exitId), ("i", instanceId));
        }

        /// <summary>Deletes one PhaseGoto instance and its subtype row from the node's sequence.</summary>
        public static void RemovePhaseGoto(DbConnection connection, string instanceId)
        {
            Sql.Execute(connection, null,
                "DELETE FROM action_instance WHERE id = @i;", ("i", instanceId));
        }

        private static string GetActionSequenceId(DbConnection connection, string nodeId)
        {
            var sequenceId = "";
            Sql.QueryAll(connection,
                "SELECT action_sequence_id FROM phase_node_action WHERE node_id = @node;",
                reader => sequenceId = reader.GetString(0),
                ("node", nodeId));
            if (string.IsNullOrEmpty(sequenceId))
            {
                throw new InvalidOperationException($"Node '{nodeId}' is not an Action node (no sequence).");
            }
            return sequenceId;
        }

        private static int NextInstanceOrdinal(DbConnection connection, string sequenceId)
        {
            var ordinal = 0;
            Sql.QueryAll(connection,
                "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM action_instance WHERE action_sequence_id = @seq;",
                reader => ordinal = reader.GetInt32(0),
                ("seq", sequenceId));
            return ordinal;
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
        /// <remarks>
        /// Import/restore API only. Normal Workbench edits use AddNode, remove,
        /// and semantic commands so stable IDs and host extension rows are not
        /// replaced wholesale.
        /// </remarks>
        internal static void ReplaceGraph(DbConnection connection, string sessionId, SessionGraphDefinition graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            var startCount = 0;
            foreach (var node in graph.Nodes)
                if (node is SessionStartNodeDefinition) startCount++;
            if (startCount != 1)
                throw new InvalidOperationException($"Session '{sessionId}' must have exactly one Start node (found {startCount}).");

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
            var nodeType = ReadNodeType(connection, nodeId, sessionId);
            if (nodeType == "Start")
                throw new InvalidOperationException("The Session Start node is singular and cannot be deleted.");
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

            if (node is SessionStartNodeDefinition)
            {
                var existing = Convert.ToInt32(SqlScalar(connection,
                    "SELECT COUNT(*) FROM session_graph_node WHERE session_id = @session AND node_type = 'Start';",
                    ("session", sessionId)));
                if (existing > 0)
                    throw new InvalidOperationException("A session may contain exactly one Start node.");
            }

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

        private static string ReadNodeType(DbConnection connection, string nodeId, string sessionId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT node_type FROM session_graph_node WHERE id = @node AND session_id = @session;";
                var node = command.CreateParameter(); node.ParameterName = "node"; node.Value = nodeId; command.Parameters.Add(node);
                var session = command.CreateParameter(); session.ParameterName = "session"; session.Value = sessionId; command.Parameters.Add(session);
                return Convert.ToString(command.ExecuteScalar());
            }
        }

        private static object SqlScalar(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                foreach (var parameter in parameters)
                {
                    var dbParameter = command.CreateParameter();
                    dbParameter.ParameterName = parameter.Name;
                    dbParameter.Value = parameter.Value ?? DBNull.Value;
                    command.Parameters.Add(dbParameter);
                }
                return command.ExecuteScalar();
            }
        }
    }
}
