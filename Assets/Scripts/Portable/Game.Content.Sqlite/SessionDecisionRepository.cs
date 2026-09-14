using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 16: SessionDecision authoring — prompt edits, 1-3 option rows each
    /// owning an ordered ActionSequence — plus the unique SessionGoto behavior:
    /// adding a SessionGoto instance creates, in ONE transaction, the instance and
    /// its unique session_node_output port (session_goto kind); deleting the
    /// instance cascades the port and any edge wired from it. Multiple instances
    /// with the same displayed label remain distinct sockets because identity is
    /// the instance id.
    /// </summary>
    public static class SessionDecisionRepository
    {
        public static void UpdatePrompt(DbConnection connection, string nodeId, string prompt)
        {
            Sql.Execute(connection, null,
                "UPDATE session_node_decision SET prompt = @prompt WHERE node_id = @node;",
                ("prompt", prompt ?? ""), ("node", nodeId));
        }

        /// <summary>Adds an option row owning a fresh empty sequence.</summary>
        public static void AddOption(DbConnection connection, string nodeId, string optionId, string label, string sequenceId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException("Node id required.", nameof(nodeId));
            if (string.IsNullOrEmpty(optionId)) throw new ArgumentException("Option id required.", nameof(optionId));
            if (string.IsNullOrEmpty(sequenceId)) throw new ArgumentException("Sequence id required.", nameof(sequenceId));

            var ordinal = NextOptionOrdinal(connection, nodeId);
            Sql.Execute(connection, null,
                "INSERT INTO action_sequence (id) VALUES (@id) ON CONFLICT(id) DO NOTHING;",
                ("id", sequenceId));
            Sql.Execute(connection, null,
                "INSERT INTO session_decision_option (id, node_id, ordinal, label, action_sequence_id) " +
                "VALUES (@id, @node, @ordinal, @label, @seq);",
                ("id", optionId), ("node", nodeId), ("ordinal", ordinal),
                ("label", (object)label ?? ""), ("seq", sequenceId));
        }

        /// <summary>
        /// Deletes an option and everything it owns. The option's action_sequence
        /// is deleted explicitly: its ON DELETE CASCADE chain removes the option
        /// row, its action instances, their session-goto ports and any edges wired
        /// from those ports — all in one transactional statement.
        /// </summary>
        public static void RemoveOption(DbConnection connection, string nodeId, string optionId)
        {
            Sql.Execute(connection, null,
                "DELETE FROM action_sequence WHERE id = (SELECT action_sequence_id FROM session_decision_option " +
                "WHERE id = @option AND node_id = @node);",
                ("option", optionId), ("node", nodeId));
        }

        public static void RenameOption(DbConnection connection, string optionId, string label)
        {
            Sql.Execute(connection, null,
                "UPDATE session_decision_option SET label = @label WHERE id = @option;",
                ("label", (object)label ?? ""), ("option", optionId));
        }

        /// <summary>
        /// Adds one SessionGoto Action Instance + its unique output port in a
        /// single transaction. The port appears on the decision node immediately
        /// (id = {optionId}-goto-{n}); its label is copied to the socket so the
        /// inline row edit renames the visible port.
        /// </summary>
        public static string AddSessionGoto(DbConnection connection, string nodeId, string optionId, string label)
        {
            if (string.IsNullOrEmpty(optionId)) throw new ArgumentException("Option id required.", nameof(optionId));
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException("Node id required.", nameof(nodeId));

            var sequenceId = OptionSequenceId(connection, optionId);
            var instanceId = optionId + "-goto-" + NextInstanceOrdinal(connection, sequenceId);
            var portId = instanceId + "-port";

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                        "VALUES (@id, @seq, @ordinal, @type, 1);",
                        ("id", instanceId), ("seq", sequenceId), ("ordinal", NextInstanceOrdinal(connection, sequenceId)),
                        ("type", ActionType.SessionGotoV2));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_session_goto (action_instance_id, label) VALUES (@i, @label);",
                        ("i", instanceId), ("label", (object)label ?? ""));
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) " +
                        "VALUES (@id, @node, @kind, @ordinal, @label, NULL, @goto);",
                        ("id", portId), ("node", nodeId), ("kind", DatabaseInitializer.PortKindName(GraphPortKind.SessionGoto)),
                        ("ordinal", NextOutputOrdinal(connection, nodeId)),
                        ("label", (object)label ?? ""), ("goto", instanceId));
                    transaction.Commit();
                    return instanceId;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Renames the SessionGoto instance label AND its projected port label (live).</summary>
        public static void SetSessionGotoLabel(DbConnection connection, string instanceId, string label)
        {
            Sql.Execute(connection, null,
                "UPDATE action_instance_session_goto SET label = @label WHERE action_instance_id = @i;",
                ("label", (object)label ?? ""), ("i", instanceId));
            Sql.Execute(connection, null,
                "UPDATE session_node_output SET label = @label WHERE session_goto_action_instance_id = @i;",
                ("label", (object)label ?? ""), ("i", instanceId));
        }

        /// <summary>
        /// Deletes a SessionGoto instance; the schema cascades its subtype row,
        /// its unique output port and any edge wired from that port (transactional).
        /// </summary>
        public static void RemoveSessionGoto(DbConnection connection, string instanceId)
        {
            Sql.Execute(connection, null, "DELETE FROM action_instance WHERE id = @i;", ("i", instanceId));
        }

        /// <summary>SessionGoto instances in a decision option's sequence, in order.</summary>
        public static List<(string InstanceId, string Label)> ListSessionGotos(DbConnection connection, string optionId)
        {
            var result = new List<(string, string)>();
            var sequenceId = OptionSequenceId(connection, optionId);
            Sql.QueryAll(connection,
                "SELECT ai.id, g.label FROM action_instance ai " +
                "JOIN action_instance_session_goto g ON g.action_instance_id = ai.id " +
                "WHERE ai.action_sequence_id = @seq ORDER BY ai.ordinal;",
                reader => result.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1))),
                ("seq", sequenceId));
            return result;
        }

        private static string OptionSequenceId(DbConnection connection, string optionId)
        {
            var sequenceId = "";
            Sql.QueryAll(connection,
                "SELECT action_sequence_id FROM session_decision_option WHERE id = @option;",
                reader => sequenceId = reader.GetString(0),
                ("option", optionId));
            if (string.IsNullOrEmpty(sequenceId))
            {
                throw new InvalidOperationException($"Decision option '{optionId}' has no sequence.");
            }
            return sequenceId;
        }

        private static int NextOptionOrdinal(DbConnection connection, string nodeId)
        {
            var ordinal = 0;
            Sql.QueryAll(connection,
                "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM session_decision_option WHERE node_id = @node;",
                reader => ordinal = reader.GetInt32(0),
                ("node", nodeId));
            return ordinal;
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

        private static int NextOutputOrdinal(DbConnection connection, string nodeId)
        {
            var ordinal = 0;
            Sql.QueryAll(connection,
                "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM session_node_output WHERE node_id = @node;",
                reader => ordinal = reader.GetInt32(0),
                ("node", nodeId));
            return ordinal;
        }
    }
}
