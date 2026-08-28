using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    // ------------------------------------------------------------------
    // Snapshot records (Ticket 18). Commands capture these BEFORE a
    // destructive edit and restore them on Undo through repository
    // operations — never raw inverse SQL.
    // ------------------------------------------------------------------

    /// <summary>Everything needed to restore one PhaseGoto instance.</summary>
    public sealed class PhaseGotoSnapshot
    {
        public string InstanceId;
        public string SequenceId;
        public int Ordinal;
        public string ExitId;
        public bool IsBlocking;
    }

    /// <summary>Everything needed to restore one SessionGoto instance (including its unique port + edges).</summary>
    public sealed class SessionGotoSnapshot
    {
        public string InstanceId;
        public string SequenceId;
        public int Ordinal;
        public string Label;
        public string NodeId;
        public string PortId;
        public int PortOrdinal;
        public List<string> EdgeTargets = new List<string>();
    }

    /// <summary>Everything needed to restore one SessionDecision option row and its owned gotos.</summary>
    public sealed class SessionOptionSnapshot
    {
        public string NodeId;
        public string OptionId;
        public string Label;
        public string SequenceId;
        public int Ordinal;
        public List<SessionGotoSnapshot> Gotos = new List<SessionGotoSnapshot>();
    }

    /// <summary>Everything needed to restore one PhaseDecision option row and its owned gotos.</summary>
    public sealed class PhaseOptionSnapshot
    {
        public string NodeId;
        public string OptionId;
        public string Label;
        public string SequenceId;
        public int Ordinal;
        public List<PhaseGotoSnapshot> Gotos = new List<PhaseGotoSnapshot>();
    }

    /// <summary>
    /// Read/restore operations behind the Ticket 18 semantic undo layer.
    /// Snapshot methods capture state before a destructive edit; Restore
    /// methods re-insert captured rows (new edge ids) inside one transaction.
    /// </summary>
    public static class AuthoringUndo
    {
        // ---- PhaseGoto ----

        public static PhaseGotoSnapshot SnapshotPhaseGoto(DbConnection connection, string instanceId)
        {
            var snapshot = new PhaseGotoSnapshot { InstanceId = instanceId };
            Sql.QueryAll(connection,
                "SELECT ai.action_sequence_id, ai.ordinal, ai.is_blocking, g.phase_exit_id " +
                "FROM action_instance ai JOIN action_instance_phase_goto g ON g.action_instance_id = ai.id " +
                "WHERE ai.id = @i;",
                reader =>
                {
                    snapshot.SequenceId = reader.GetString(0);
                    snapshot.Ordinal = reader.GetInt32(1);
                    snapshot.IsBlocking = reader.GetInt32(2) != 0;
                    snapshot.ExitId = reader.GetString(3);
                },
                ("i", instanceId));
            if (snapshot.SequenceId == null)
            {
                throw new InvalidOperationException($"PhaseGoto '{instanceId}' not found.");
            }
            return snapshot;
        }

        /// <summary>Inserts a PhaseGoto instance at an exact position (restore path).</summary>
        public static void InsertPhaseGoto(DbConnection connection, string instanceId, string sequenceId, int ordinal, string exitId, bool isBlocking)
        {
            Sql.Execute(connection, null,
                "INSERT OR IGNORE INTO action_sequence (id) VALUES (@seq);",
                ("seq", sequenceId));
            Sql.Execute(connection, null,
                "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                "VALUES (@id, @seq, @ordinal, @type, @block);",
                ("id", instanceId), ("seq", sequenceId), ("ordinal", ordinal),
                ("type", ActionType.PhaseGotoV2), ("block", isBlocking ? 1 : 0));
            Sql.Execute(connection, null,
                "INSERT INTO action_instance_phase_goto (action_instance_id, phase_exit_id) VALUES (@i, @exit);",
                ("i", instanceId), ("exit", exitId));
        }

        public static void RestorePhaseGoto(DbConnection connection, PhaseGotoSnapshot snapshot)
        {
            InsertPhaseGoto(connection, snapshot.InstanceId, snapshot.SequenceId,
                snapshot.Ordinal, snapshot.ExitId, snapshot.IsBlocking);
        }

        public static string GetPhaseGotoExit(DbConnection connection, string instanceId)
        {
            var exitId = "";
            Sql.QueryAll(connection,
                "SELECT phase_exit_id FROM action_instance_phase_goto WHERE action_instance_id = @i;",
                reader => exitId = reader.GetString(0), ("i", instanceId));
            return exitId;
        }

        /// <summary>The Action node's owned sequence id (phase scope).</summary>
        public static string NodeActionSequenceId(DbConnection connection, string nodeId)
        {
            var sequenceId = "";
            Sql.QueryAll(connection,
                "SELECT action_sequence_id FROM phase_node_action WHERE node_id = @node;",
                reader => sequenceId = reader.GetString(0), ("node", nodeId));
            if (string.IsNullOrEmpty(sequenceId))
            {
                throw new InvalidOperationException($"Node '{nodeId}' is not an Action node (no sequence).");
            }
            return sequenceId;
        }

        // ---- SessionGoto ----

        public static SessionGotoSnapshot SnapshotSessionGoto(DbConnection connection, string instanceId)
        {
            var snapshot = new SessionGotoSnapshot { InstanceId = instanceId };
            Sql.QueryAll(connection,
                "SELECT ai.action_sequence_id, ai.ordinal, g.label " +
                "FROM action_instance ai JOIN action_instance_session_goto g ON g.action_instance_id = ai.id " +
                "WHERE ai.id = @i;",
                reader =>
                {
                    snapshot.SequenceId = reader.GetString(0);
                    snapshot.Ordinal = reader.GetInt32(1);
                    snapshot.Label = reader.IsDBNull(2) ? "" : reader.GetString(2);
                },
                ("i", instanceId));
            if (snapshot.SequenceId == null)
            {
                throw new InvalidOperationException($"SessionGoto '{instanceId}' not found.");
            }
            Sql.QueryAll(connection,
                "SELECT id, node_id, ordinal FROM session_node_output WHERE session_goto_action_instance_id = @i;",
                reader =>
                {
                    snapshot.PortId = reader.GetString(0);
                    snapshot.NodeId = reader.GetString(1);
                    snapshot.PortOrdinal = reader.GetInt32(2);
                },
                ("i", instanceId));
            if (!string.IsNullOrEmpty(snapshot.PortId))
            {
                Sql.QueryAll(connection,
                    "SELECT target_node_id FROM session_graph_edge WHERE source_port_id = @port;",
                    reader => snapshot.EdgeTargets.Add(reader.GetString(0)),
                    ("port", snapshot.PortId));
            }
            return snapshot;
        }

        /// <summary>Restores a SessionGoto instance + its unique port + any edges, one transaction.</summary>
        public static void RestoreSessionGoto(DbConnection connection, SessionGotoSnapshot snapshot)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT OR IGNORE INTO action_sequence (id) VALUES (@seq);", ("seq", snapshot.SequenceId));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                        "VALUES (@id, @seq, @ordinal, @type, 1);",
                        ("id", snapshot.InstanceId), ("seq", snapshot.SequenceId), ("ordinal", snapshot.Ordinal),
                        ("type", ActionType.SessionGotoV2));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_session_goto (action_instance_id, label) VALUES (@i, @label);",
                        ("i", snapshot.InstanceId), ("label", (object)snapshot.Label ?? ""));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) " +
                        "VALUES (@id, @node, 'session_goto', @ordinal, @label, NULL, @i);",
                        ("id", snapshot.PortId), ("node", snapshot.NodeId), ("ordinal", snapshot.PortOrdinal),
                        ("label", (object)snapshot.Label ?? ""), ("i", snapshot.InstanceId));
                    foreach (var target in snapshot.EdgeTargets)
                    {
                        Sql.Execute(connection, transaction,
                            "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) " +
                            "SELECT 'se-' || lower(hex(randomblob(8))), session_id, @port, @target " +
                            "FROM session_graph_node WHERE id = @target;",
                            ("port", snapshot.PortId), ("target", target));
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

        public static string GetSessionGotoLabel(DbConnection connection, string instanceId)
        {
            var label = "";
            Sql.QueryAll(connection,
                "SELECT label FROM action_instance_session_goto WHERE action_instance_id = @i;",
                reader => label = reader.IsDBNull(0) ? "" : reader.GetString(0), ("i", instanceId));
            return label;
        }

        /// <summary>The decision option's owned sequence id (session scope).</summary>
        public static string SessionOptionSequenceId(DbConnection connection, string optionId)
        {
            return DecisionOptionSequenceId(connection, "session_decision_option", optionId);
        }

        /// <summary>The decision option's owned sequence id (phase scope).</summary>
        public static string PhaseOptionSequenceId(DbConnection connection, string optionId)
        {
            return DecisionOptionSequenceId(connection, "phase_decision_option", optionId);
        }

        private static string DecisionOptionSequenceId(DbConnection connection, string table, string optionId)
        {
            var sequenceId = "";
            Sql.QueryAll(connection,
                $"SELECT action_sequence_id FROM {table} WHERE id = @option;",
                reader => sequenceId = reader.GetString(0), ("option", optionId));
            if (string.IsNullOrEmpty(sequenceId))
            {
                throw new InvalidOperationException($"Decision option '{optionId}' has no sequence.");
            }
            return sequenceId;
        }

        // ---- decision options ----

        public static SessionOptionSnapshot SnapshotSessionOption(DbConnection connection, string nodeId, string optionId)
        {
            var snapshot = new SessionOptionSnapshot { NodeId = nodeId, OptionId = optionId };
            Sql.QueryAll(connection,
                "SELECT label, ordinal, action_sequence_id FROM session_decision_option WHERE id = @option AND node_id = @node;",
                reader =>
                {
                    snapshot.Label = reader.GetString(0);
                    snapshot.Ordinal = reader.GetInt32(1);
                    snapshot.SequenceId = reader.GetString(2);
                },
                ("option", optionId), ("node", nodeId));
            if (snapshot.SequenceId == null)
            {
                throw new InvalidOperationException($"Session decision option '{optionId}' not found.");
            }
            Sql.QueryAll(connection,
                "SELECT id FROM action_instance WHERE action_sequence_id = @seq ORDER BY ordinal;",
                reader =>
                {
                    var instanceId = reader.GetString(0);
                    snapshot.Gotos.Add(SnapshotSessionGoto(connection, instanceId));
                },
                ("seq", snapshot.SequenceId));
            return snapshot;
        }

        public static void RestoreSessionOption(DbConnection connection, SessionOptionSnapshot snapshot)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT OR IGNORE INTO action_sequence (id) VALUES (@seq);", ("seq", snapshot.SequenceId));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session_decision_option (id, node_id, ordinal, label, action_sequence_id) " +
                        "VALUES (@id, @node, @ordinal, @label, @seq);",
                        ("id", snapshot.OptionId), ("node", snapshot.NodeId), ("ordinal", snapshot.Ordinal),
                        ("label", snapshot.Label), ("seq", snapshot.SequenceId));
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            foreach (var gotoSnapshot in snapshot.Gotos)
            {
                RestoreSessionGoto(connection, gotoSnapshot);
            }
        }

        public static PhaseOptionSnapshot SnapshotPhaseOption(DbConnection connection, string nodeId, string optionId)
        {
            var snapshot = new PhaseOptionSnapshot { NodeId = nodeId, OptionId = optionId };
            Sql.QueryAll(connection,
                "SELECT label, ordinal, action_sequence_id FROM phase_decision_option WHERE id = @option AND node_id = @node;",
                reader =>
                {
                    snapshot.Label = reader.GetString(0);
                    snapshot.Ordinal = reader.GetInt32(1);
                    snapshot.SequenceId = reader.GetString(2);
                },
                ("option", optionId), ("node", nodeId));
            if (snapshot.SequenceId == null)
            {
                throw new InvalidOperationException($"Phase decision option '{optionId}' not found.");
            }
            Sql.QueryAll(connection,
                "SELECT id FROM action_instance WHERE action_sequence_id = @seq ORDER BY ordinal;",
                reader => snapshot.Gotos.Add(SnapshotPhaseGoto(connection, reader.GetString(0))),
                ("seq", snapshot.SequenceId));
            return snapshot;
        }

        public static void RestorePhaseOption(DbConnection connection, PhaseOptionSnapshot snapshot)
        {
            Sql.Execute(connection, null,
                "INSERT OR IGNORE INTO action_sequence (id) VALUES (@seq);", ("seq", snapshot.SequenceId));
            Sql.Execute(connection, null,
                "INSERT INTO phase_decision_option (id, node_id, ordinal, label, action_sequence_id) " +
                "VALUES (@id, @node, @ordinal, @label, @seq);",
                ("id", snapshot.OptionId), ("node", snapshot.NodeId), ("ordinal", snapshot.Ordinal),
                ("label", snapshot.Label), ("seq", snapshot.SequenceId));
            foreach (var gotoSnapshot in snapshot.Gotos)
            {
                RestorePhaseGoto(connection, gotoSnapshot);
            }
        }

        // ---- reads ----

        public static string GetDecisionPrompt(DbConnection connection, string scope, string nodeId)
        {
            var table = scope == "session" ? "session_node_decision" : "phase_node_decision";
            var prompt = "";
            Sql.QueryAll(connection,
                $"SELECT prompt FROM {table} WHERE node_id = @node;",
                reader => prompt = reader.IsDBNull(0) ? "" : reader.GetString(0), ("node", nodeId));
            return prompt;
        }

        public static (string Source, string Key, string Op, float Value) GetVariableCheck(DbConnection connection, string nodeId)
        {
            var result = ("progress", "", ">=", 0f);
            Sql.QueryAll(connection,
                "SELECT source_kind, variable_key, compare_operator, compare_value " +
                "FROM phase_node_variable_check WHERE node_id = @node;",
                reader =>
                {
                    result = (
                        reader.IsDBNull(0) ? "progress" : reader.GetString(0),
                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                        reader.IsDBNull(2) ? ">=" : reader.GetString(2),
                        reader.GetFloat(3));
                },
                ("node", nodeId));
            return result;
        }

        public static string GetExitName(DbConnection connection, string exitId)
        {
            var name = "";
            Sql.QueryAll(connection,
                "SELECT name FROM phase_exit WHERE id = @id;",
                reader => name = reader.IsDBNull(0) ? "" : reader.GetString(0), ("id", exitId));
            return name;
        }

        // ---- edges ----

        public static List<GraphEdgeDefinition> SessionEdgesTouchingNode(DbConnection connection, string sessionId, string nodeId)
        {
            return EdgesTouchingNode(connection, "session_graph_edge", "session_graph_node", "session_id",
                sessionId, nodeId, "session_node_output");
        }

        public static List<GraphEdgeDefinition> PhaseEdgesTouchingNode(DbConnection connection, string phaseId, string nodeId)
        {
            return EdgesTouchingNode(connection, "phase_graph_edge", "phase_graph_node", "phase_id",
                phaseId, nodeId, null);
        }

        private static List<GraphEdgeDefinition> EdgesTouchingNode(
            DbConnection connection, string edgeTable, string nodeTable, string parentColumn,
            string parentId, string nodeId, string outputTable)
        {
            var result = new List<GraphEdgeDefinition>();
            var sourcePredicate = outputTable == null
                ? $"e.source_port_id IN (SELECT o.id FROM phase_node_output o WHERE o.node_id = @node)"
                : $"e.source_port_id IN (SELECT o.id FROM {outputTable} o WHERE o.node_id = @node)";
            Sql.QueryAll(connection,
                $"SELECT e.id, e.source_port_id, e.target_node_id FROM {edgeTable} e " +
                $"WHERE e.{parentColumn} = @parent AND (e.target_node_id = @node OR {sourcePredicate});",
                reader => result.Add(new GraphEdgeDefinition
                {
                    Id = reader.GetString(0),
                    SourceOutputId = reader.GetString(1),
                    TargetNodeId = reader.GetString(2),
                }),
                ("parent", parentId), ("node", nodeId));
            return result;
        }

        /// <summary>Edge targets leaving a socket (used to restore a replaced connect).</summary>
        public static List<(string PortId, string TargetNodeId)> EdgesFromSource(DbConnection connection, string sourceOutputId)
        {
            var result = new List<(string, string)>();
            Sql.QueryAll(connection,
                "SELECT source_port_id, target_node_id FROM session_graph_edge WHERE source_port_id = @source " +
                "UNION ALL SELECT source_port_id, target_node_id FROM phase_graph_edge WHERE source_port_id = @source;",
                reader => result.Add((reader.GetString(0), reader.GetString(1))),
                ("source", sourceOutputId));
            return result;
        }

        /// <summary>Edges wired from the projected sockets of one exit (restore on exit-delete undo).</summary>
        public static List<(string PortId, string TargetNodeId)> ExitProjectedEdges(DbConnection connection, string exitId)
        {
            var result = new List<(string, string)>();
            Sql.QueryAll(connection,
                "SELECT e.source_port_id, e.target_node_id FROM session_graph_edge e " +
                "JOIN session_node_output o ON o.id = e.source_port_id " +
                "WHERE o.phase_exit_id = @exit;",
                reader => result.Add((reader.GetString(0), reader.GetString(1))),
                ("exit", exitId));
            return result;
        }

        public static string GetOptionLabel(DbConnection connection, string scope, string optionId)
        {
            var table = scope == "session" ? "session_decision_option" : "phase_decision_option";
            var label = "";
            Sql.QueryAll(connection,
                $"SELECT label FROM {table} WHERE id = @option;",
                reader => label = reader.GetString(0), ("option", optionId));
            return label;
        }

        /// <summary>The next free ordinal in an action sequence (deterministic instance-id computation).</summary>
        public static int NextInstanceOrdinal(DbConnection connection, string sequenceId)
        {
            var ordinal = 0;
            Sql.QueryAll(connection,
                "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM action_instance WHERE action_sequence_id = @seq;",
                reader => ordinal = reader.GetInt32(0),
                ("seq", sequenceId));
            return ordinal;
        }

        /// <summary>The projected output map of a placement: port id -> phase exit id.</summary>
        public static Dictionary<string, string> PlacementExitMap(DbConnection connection, string placementNodeId)
        {
            var result = new Dictionary<string, string>();
            Sql.QueryAll(connection,
                "SELECT id, phase_exit_id FROM session_node_output WHERE node_id = @node AND phase_exit_id IS NOT NULL;",
                reader => result[reader.GetString(0)] = reader.GetString(1),
                ("node", placementNodeId));
            return result;
        }

        // ---- Make Unique revert ----

        /// <summary>
        /// Undo of Make Unique: re-points the placement back at the shared phase
        /// and restores the original projected exit mappings, preserving port ids
        /// and their edges (the exact inverse of MakeUniqueRepository.MakeUnique).
        /// The caller deletes the now-orphaned clone phase afterwards.
        /// </summary>
        public static void RevertPlacement(DbConnection connection, string placementNodeId, string sharedPhaseId, Dictionary<string, string> portToOldExit)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "UPDATE session_node_phase SET phase_id = @phase WHERE node_id = @node;",
                        ("phase", sharedPhaseId), ("node", placementNodeId));
                    foreach (var pair in portToOldExit)
                    {
                        Sql.Execute(connection, transaction,
                            "UPDATE session_node_output SET phase_exit_id = @exit WHERE id = @port;",
                            ("exit", pair.Value), ("port", pair.Key));
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
    }
}
