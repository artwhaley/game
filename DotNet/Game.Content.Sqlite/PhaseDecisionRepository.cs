using System;
using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 16: PhaseDecision authoring — prompt edits and 1-3 option rows each
    /// owning an ordered ActionSequence. Phase-scope options use the Phase Action
    /// palette; PhaseGoto there reuses the shared PhaseExit dropdown and does NOT
    /// create an internal decision output (the decision node keeps its common
    /// normal output).
    /// </summary>
    public static class PhaseDecisionRepository
    {
        public static void UpdatePrompt(DbConnection connection, string nodeId, string prompt)
        {
            Sql.Execute(connection, null,
                "UPDATE phase_node_decision SET prompt = @prompt WHERE node_id = @node;",
                ("prompt", prompt ?? ""), ("node", nodeId));
        }

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
                "INSERT INTO phase_decision_option (id, node_id, ordinal, label, action_sequence_id) " +
                "VALUES (@id, @node, @ordinal, @label, @seq);",
                ("id", optionId), ("node", nodeId), ("ordinal", ordinal),
                ("label", (object)label ?? ""), ("seq", sequenceId));
        }

        public static void RemoveOption(DbConnection connection, string nodeId, string optionId)
        {
            Sql.Execute(connection, null,
                "DELETE FROM phase_decision_option WHERE id = @option AND node_id = @node;",
                ("option", optionId), ("node", nodeId));
        }

        public static void RenameOption(DbConnection connection, string optionId, string label)
        {
            Sql.Execute(connection, null,
                "UPDATE phase_decision_option SET label = @label WHERE id = @option;",
                ("label", (object)label ?? ""), ("option", optionId));
        }

        private static int NextOptionOrdinal(DbConnection connection, string nodeId)
        {
            var ordinal = 0;
            Sql.QueryAll(connection,
                "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM phase_decision_option WHERE node_id = @node;",
                reader => ordinal = reader.GetInt32(0),
                ("node", nodeId));
            return ordinal;
        }
    }
}
