using System;
using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Migration 2's in-transaction data transformation. Runs after every DDL
    /// statement of SQLITE-SCHEMA-V2.sql, inside the same transaction, written
    /// against DbConnection/DbTransaction only (provider-neutral).
    ///
    /// Contract (Docs/GraphWorkbench/03-schema-v2-design.md, migration posture):
    /// - seed the default session type + temperature definition;
    /// - assign every existing Session to the seeded type;
    /// - capture legacy PhaseSlot placements, then CLEAR slot rows so phase
    ///   deletion is no longer blocked by their RESTRICT candidate references;
    /// - give every Phase the standard executable graph with Complete + Fail exits;
    /// - transform legacy configured Card Actions into owned per-Card Action
    ///   Instances on a fresh action_sequence per Card, appending the default
    ///   IncrementProgress(10);
    /// - rebuild every Session as Start → referenced phases → End with Complete
    ///   continuing forward and Fail going straight to SessionEnd.
    ///
    /// Every statement tolerates an empty legacy database (fresh installs run the
    /// same statements harmlessly). Unknown host tables are never touched.
    /// </summary>
    internal static class Migration2Transform
    {
        public const string DefaultSessionTypeId = "type-standard";
        public const string DefaultSessionTypeTitle = "Standard";
        public const string HappinessTemperatureId = "happiness";
        private const float CardDefaultProgressAmount = 10f;

        public static void Transform(DbConnection connection, DbTransaction transaction)
        {
            SeedSessionType(connection, transaction);
            SeedTemperatureDefinition(connection, transaction);
            AssignSessionTypes(connection, transaction);

            var placementsBySession = CaptureLegacyPlacements(connection, transaction);

            ClearLegacySlotRows(connection, transaction);
            TransformPhasesToStandardGraphs(connection, transaction);
            TransformCardActionsIntoInstances(connection, transaction);
            InsertSessionGraphs(connection, transaction, placementsBySession);
        }

        // ---- seeds ----

        private static void SeedSessionType(DbConnection connection, DbTransaction transaction)
        {
            Execute(connection, transaction,
                "INSERT OR IGNORE INTO session_type (id, title) VALUES (@id, @title);",
                Param("id", DefaultSessionTypeId), Param("title", DefaultSessionTypeTitle));
        }

        private static void SeedTemperatureDefinition(DbConnection connection, DbTransaction transaction)
        {
            Execute(connection, transaction,
                "INSERT OR IGNORE INTO temperature_definition (id, title, min_value, max_value, default_value) " +
                "VALUES (@id, @title, @min, @max, @defaultValue);",
                Param("id", HappinessTemperatureId), Param("title", "Happiness"),
                Param("min", 0d), Param("max", 100d), Param("defaultValue", 50d));
        }

        private static void AssignSessionTypes(DbConnection connection, DbTransaction transaction)
        {
            Execute(connection, transaction,
                "UPDATE session SET session_type_id = @type WHERE session_type_id IS NULL;",
                Param("type", DefaultSessionTypeId));
        }

        // ---- legacy placement capture + slot clear ----

        /// <summary>Reads sessionId → ordered referenced phaseIds from slot rows (first candidate of each slot) BEFORE they are cleared.</summary>
        private static Dictionary<string, List<string>> CaptureLegacyPlacements(DbConnection connection, DbTransaction transaction)
        {
            var placementsBySession = new Dictionary<string, List<string>>();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "SELECT ps.session_id, psc.phase_id " +
                    "FROM phase_slot ps " +
                    "JOIN phase_slot_candidate psc ON psc.phase_slot_id = ps.id AND psc.ordinal = 0 " +
                    "ORDER BY ps.session_id ASC, ps.ordinal ASC;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var sessionId = reader.GetString(0);
                        var phaseId = reader.GetString(1);
                        if (!placementsBySession.TryGetValue(sessionId, out var phases))
                        {
                            phases = new List<string>();
                            placementsBySession.Add(sessionId, phases);
                        }
                        phases.Add(phaseId);
                    }
                }
            }

            return placementsBySession;
        }

        /// <summary>
        /// Empties the obsolete slot structures. Required: phase_slot_candidate rows
        /// hold RESTRICT references into phase that would otherwise block phase
        /// deletion for content that no longer uses slots at all.
        /// </summary>
        private static void ClearLegacySlotRows(DbConnection connection, DbTransaction transaction)
        {
            Execute(connection, transaction, "DELETE FROM phase_slot_candidate;");
            Execute(connection, transaction, "DELETE FROM phase_slot;");
        }

        // ---- phases -> standard executable graph ----

        private static void TransformPhasesToStandardGraphs(DbConnection connection, DbTransaction transaction)
        {
            var phaseIds = new List<string>();
            ReadAll(connection, transaction, "SELECT id FROM phase ORDER BY id;", reader => phaseIds.Add(reader.GetString(0)));

            foreach (var phaseId in phaseIds)
            {
                InsertStandardPhaseGraph(connection, transaction, phaseId);
            }
        }

        /// <summary>
        /// The standard migrated low graph from the schema contract:
        /// Entry → CardExecutor → happiness&lt;10? true→GOTO Fail / false→ progress&gt;=100?
        /// true→GOTO Complete / false→ back to the CardExecutor (user-paced loop).
        /// Exits: Complete and Fail. Deterministic ids derive from the stable phase id.
        /// </summary>
        private static void InsertStandardPhaseGraph(DbConnection connection, DbTransaction transaction, string phaseId)
        {
            var completeExit = $"px-{phaseId}-complete";
            var failExit = $"px-{phaseId}-fail";

            Execute(connection, transaction,
                "INSERT INTO phase_exit (id, phase_id, ordinal, name) VALUES (@id, @phase, 0, 'Complete');",
                Param("id", completeExit), Param("phase", phaseId));
            Execute(connection, transaction,
                "INSERT INTO phase_exit (id, phase_id, ordinal, name) VALUES (@id, @phase, 1, 'Fail');",
                Param("id", failExit), Param("phase", phaseId));

            var entry = $"pn-{phaseId}-entry";
            var draw = $"pn-{phaseId}-draw";
            var failCheck = $"pn-{phaseId}-failcheck";
            var doneCheck = $"pn-{phaseId}-donecheck";

            InsertPhaseNode(connection, transaction, entry, phaseId, "Entry");
            PhaseNodeOutput(connection, transaction, $"{entry}-out", entry, "normal", 0);

            InsertPhaseNode(connection, transaction, draw, phaseId, "CardExecutor");
            PhaseNodeOutput(connection, transaction, $"{draw}-out", draw, "normal", 0);

            InsertPhaseNode(connection, transaction, failCheck, phaseId, "VariableCheck");
            PhaseNodeOutput(connection, transaction, $"{failCheck}-true", failCheck, "true", 0);
            PhaseNodeOutput(connection, transaction, $"{failCheck}-false", failCheck, "false", 1);
            Execute(connection, transaction,
                "INSERT INTO phase_node_variable_check (node_id, source_kind, variable_key, compare_operator, compare_value) " +
                "VALUES (@node, 'temperature', @key, '<', 10);",
                Param("node", failCheck), Param("key", HappinessTemperatureId));

            InsertPhaseNode(connection, transaction, doneCheck, phaseId, "VariableCheck");
            PhaseNodeOutput(connection, transaction, $"{doneCheck}-true", doneCheck, "true", 0);
            PhaseNodeOutput(connection, transaction, $"{doneCheck}-false", doneCheck, "false", 1);
            Execute(connection, transaction,
                "INSERT INTO phase_node_variable_check (node_id, source_kind, variable_key, compare_operator, compare_value) " +
                "VALUES (@node, 'progress', NULL, '>=', 100);",
                Param("node", doneCheck));

            var gotoFailSeq = $"pseq-{phaseId}-goto-fail";
            var gotoDoneSeq = $"pseq-{phaseId}-goto-done";
            Execute(connection, transaction, "INSERT INTO action_sequence (id) VALUES (@id);", Param("id", gotoFailSeq));
            Execute(connection, transaction, "INSERT INTO action_sequence (id) VALUES (@id);", Param("id", gotoDoneSeq));

            var gotoFailInstance = $"pi-{phaseId}-goto-fail";
            var gotoDoneInstance = $"pi-{phaseId}-goto-done";
            InsertInstanceRow(connection, transaction, gotoFailInstance, gotoFailSeq, 0, ActionType.PhaseGotoV2);
            Execute(connection, transaction,
                "INSERT INTO action_instance_phase_goto (action_instance_id, phase_exit_id) VALUES (@i, @exit);",
                Param("i", gotoFailInstance), Param("exit", failExit));
            InsertInstanceRow(connection, transaction, gotoDoneInstance, gotoDoneSeq, 0, ActionType.PhaseGotoV2);
            Execute(connection, transaction,
                "INSERT INTO action_instance_phase_goto (action_instance_id, phase_exit_id) VALUES (@i, @exit);",
                Param("i", gotoDoneInstance), Param("exit", completeExit));

            var gotoFailNode = $"pan-{phaseId}-goto-fail";
            var gotoDoneNode = $"pan-{phaseId}-goto-done";
            InsertPhaseNode(connection, transaction, gotoFailNode, phaseId, "Action");
            PhaseNodeOutput(connection, transaction, $"{gotoFailNode}-out", gotoFailNode, "normal", 0);
            Execute(connection, transaction,
                "INSERT INTO phase_node_action (node_id, action_sequence_id) VALUES (@node, @seq);",
                Param("node", gotoFailNode), Param("seq", gotoFailSeq));

            InsertPhaseNode(connection, transaction, gotoDoneNode, phaseId, "Action");
            PhaseNodeOutput(connection, transaction, $"{gotoDoneNode}-out", gotoDoneNode, "normal", 0);
            Execute(connection, transaction,
                "INSERT INTO phase_node_action (node_id, action_sequence_id) VALUES (@node, @seq);",
                Param("node", gotoDoneNode), Param("seq", gotoDoneSeq));

            PhaseEdge(connection, transaction, $"pe-{phaseId}-a", phaseId, $"{entry}-out", draw);
            PhaseEdge(connection, transaction, $"pe-{phaseId}-b", phaseId, $"{draw}-out", failCheck);
            PhaseEdge(connection, transaction, $"pe-{phaseId}-c", phaseId, $"{failCheck}-true", gotoFailNode);
            PhaseEdge(connection, transaction, $"pe-{phaseId}-d", phaseId, $"{failCheck}-false", doneCheck);
            PhaseEdge(connection, transaction, $"pe-{phaseId}-e", phaseId, $"{doneCheck}-true", gotoDoneNode);
            PhaseEdge(connection, transaction, $"pe-{phaseId}-f", phaseId, $"{doneCheck}-false", draw);
        }

        private static void InsertPhaseNode(DbConnection connection, DbTransaction transaction, string nodeId, string phaseId, string nodeType)
        {
            Execute(connection, transaction,
                "INSERT INTO phase_graph_node (id, phase_id, node_type) VALUES (@id, @phase, @type);",
                Param("id", nodeId), Param("phase", phaseId), Param("type", nodeType));
        }

        private static void PhaseNodeOutput(DbConnection connection, DbTransaction transaction, string outputId, string nodeId, string kind, int ordinal)
        {
            Execute(connection, transaction,
                "INSERT INTO phase_node_output (id, node_id, port_kind, ordinal, label) VALUES (@id, @node, @kind, @ordinal, NULL);",
                Param("id", outputId), Param("node", nodeId), Param("kind", kind), Param("ordinal", ordinal));
        }

        private static void PhaseEdge(DbConnection connection, DbTransaction transaction, string edgeId, string phaseId, string sourcePortId, string targetNodeId)
        {
            Execute(connection, transaction,
                "INSERT INTO phase_graph_edge (id, phase_id, source_port_id, target_node_id) VALUES (@id, @phase, @source, @target);",
                Param("id", edgeId), Param("phase", phaseId), Param("source", sourcePortId), Param("target", targetNodeId));
        }

        // ---- cards -> owned sequences of instances ----

        private static void TransformCardActionsIntoInstances(DbConnection connection, DbTransaction transaction)
        {
            var cardIds = new List<string>();
            ReadAll(connection, transaction, "SELECT id FROM card ORDER BY id;", reader => cardIds.Add(reader.GetString(0)));

            foreach (var cardId in cardIds)
            {
                var sequenceId = $"cseq-{cardId}";
                Execute(connection, transaction,
                    "INSERT INTO action_sequence (id) VALUES (@id);",
                    Param("id", sequenceId));

                var legacyActionIds = new List<string>();
                ReadAll(connection, transaction,
                    "SELECT action_id FROM card_action WHERE card_id = @card ORDER BY ordinal;",
                    reader => legacyActionIds.Add(reader.GetString(0)),
                    Param("card", cardId));

                var nextOrdinal = 0;
                for (var index = 0; index < legacyActionIds.Count; index++)
                {
                    nextOrdinal = CopyConfiguredActionAsInstance(
                        connection, transaction,
                        legacyActionIds[index], sequenceId, nextOrdinal,
                        instancePrefix: $"ci-{cardId}-{index}");
                }

                // Contract default: append IncrementProgress(+10) so migrated cards
                // still progress; it stays an ordinary editable instance afterwards.
                var autoProgressInstance = $"ci-{cardId}-autoprogress";
                InsertInstanceRow(connection, transaction, autoProgressInstance, sequenceId, nextOrdinal, ActionType.IncrementProgressV2);
                Execute(connection, transaction,
                    "INSERT INTO action_instance_increment_progress (action_instance_id, amount) VALUES (@i, @amount);",
                    Param("i", autoProgressInstance), Param("amount", CardDefaultProgressAmount));
                nextOrdinal++;

                Execute(connection, transaction,
                    "UPDATE card SET action_sequence_id = @seq WHERE id = @card;",
                    Param("seq", sequenceId), Param("card", cardId));
            }
        }

        /// <summary>Copies one legacy configured Action row (+ subtype row) as an owned Action Instance; returns the next free ordinal.</summary>
        private static int CopyConfiguredActionAsInstance(
            DbConnection connection, DbTransaction transaction,
            string actionId, string sequenceId, int ordinal, string instancePrefix)
        {
            string actionType = null;
            int isBlocking = 1;
            ReadOne(connection, transaction,
                "SELECT action_type, is_blocking FROM action WHERE id = @id;",
                reader =>
                {
                    actionType = reader.GetString(0);
                    isBlocking = reader.GetInt32(1);
                },
                Param("id", actionId));

            if (actionType == null)
            {
                throw new InvalidOperationException(
                    $"Migration2Transform: card_action references missing action '{actionId}'.");
            }

            var instanceId = $"{instancePrefix}-{actionType}";
            switch (actionType)
            {
                case ActionType.Debug:
                {
                    object message = DBNull.Value;
                    object delaySeconds = DBNull.Value;
                    ReadOne(connection, transaction,
                        "SELECT message, delay_seconds FROM action_debug WHERE action_id = @id;",
                        reader =>
                        {
                            if (!reader.IsDBNull(0)) message = reader.GetString(0);
                            if (!reader.IsDBNull(1)) delaySeconds = reader.GetDouble(1);
                        },
                        Param("id", actionId));

                    InsertInstanceRow(connection, transaction, instanceId, sequenceId, ordinal, actionType, isBlocking == 1);
                    Execute(connection, transaction,
                        "INSERT INTO action_instance_debug (action_instance_id, message, delay_seconds) VALUES (@i, @message, @delay);",
                        Param("i", instanceId), Param("message", message), Param("delay", delaySeconds));
                    break;
                }

                case ActionType.StatIncrease:
                {
                    string statKey = null;
                    double amount = 0d;
                    ReadOne(connection, transaction,
                        "SELECT stat_key, amount FROM action_stat_increase WHERE action_id = @id;",
                        reader =>
                        {
                            statKey = reader.GetString(0);
                            amount = Convert.ToDouble(reader.GetValue(1));
                        },
                        Param("id", actionId));

                    if (statKey == null)
                    {
                        throw new InvalidOperationException(
                            $"Migration2Transform: stat action '{actionId}' has no subtype row.");
                    }

                    InsertInstanceRow(connection, transaction, instanceId, sequenceId, ordinal, actionType, isBlocking == 1);
                    Execute(connection, transaction,
                        "INSERT INTO action_instance_stat_increase (action_instance_id, stat_key, amount) VALUES (@i, @key, @amount);",
                        Param("i", instanceId), Param("key", statKey), Param("amount", amount));
                    break;
                }

                case ActionType.Cutscene:
                {
                    string resourceId = null;
                    ReadOne(connection, transaction,
                        "SELECT resource_id FROM action_cutscene WHERE action_id = @id;",
                        reader => resourceId = reader.GetString(0),
                        Param("id", actionId));

                    if (resourceId == null)
                    {
                        throw new InvalidOperationException(
                            $"Migration2Transform: cutscene action '{actionId}' has no subtype row.");
                    }

                    InsertInstanceRow(connection, transaction, instanceId, sequenceId, ordinal, actionType, isBlocking == 1);
                    Execute(connection, transaction,
                        "INSERT INTO action_instance_cutscene (action_instance_id, resource_id) VALUES (@i, @resource);",
                        Param("i", instanceId), Param("resource", resourceId));
                    break;
                }

                case ActionType.Choice:
                {
                    string prompt = null;
                    ReadOne(connection, transaction,
                        "SELECT prompt FROM action_choice WHERE action_id = @id;",
                        reader => prompt = reader.GetString(0),
                        Param("id", actionId));

                    if (prompt == null)
                    {
                        throw new InvalidOperationException(
                            $"Migration2Transform: choice action '{actionId}' has no subtype row.");
                    }

                    // The v1 discriminator 'choice' is replaced by the v2
                    // prompt_choice key; the instance row must carry the v2 key
                    // so the v2 loader can read it back.
                    InsertInstanceRow(connection, transaction, instanceId, sequenceId, ordinal, ActionType.PromptChoiceV2, isBlocking == 1);
                    Execute(connection, transaction,
                        "INSERT INTO action_instance_prompt_choice (action_instance_id, prompt) VALUES (@i, @prompt);",
                        Param("i", instanceId), Param("prompt", prompt));

                    ConvertChoiceOptions(connection, transaction, choiceActionId: actionId, choiceInstanceId: instanceId);
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"Migration2Transform: unsupported legacy action type '{actionType}' on action '{actionId}'.");
            }

            return ordinal + 1;
        }

        private static void ConvertChoiceOptions(DbConnection connection, DbTransaction transaction, string choiceActionId, string choiceInstanceId)
        {
            var optionIds = new List<string>();
            ReadAll(connection, transaction,
                "SELECT id FROM choice_option WHERE choice_action_id = @choice ORDER BY ordinal;",
                reader => optionIds.Add(reader.GetString(0)),
                Param("choice", choiceActionId));

            for (var ordinal = 0; ordinal < optionIds.Count; ordinal++)
            {
                var optionId = optionIds[ordinal];
                var label = "";
                string childActionId = null;
                ReadOne(connection, transaction,
                    "SELECT label, child_action_id FROM choice_option WHERE id = @option;",
                    reader =>
                    {
                        label = reader.GetString(0);
                        if (!reader.IsDBNull(1)) childActionId = reader.GetString(1);
                    },
                    Param("option", optionId));

                var optionSequenceId = $"oseq-{optionId}";
                Execute(connection, transaction,
                    "INSERT INTO action_sequence (id) VALUES (@id);",
                    Param("id", optionSequenceId));

                if (childActionId != null)
                {
                    CopyConfiguredActionAsInstance(
                        connection, transaction,
                        childActionId, optionSequenceId, ordinal: 0,
                        instancePrefix: $"copt-{optionId}");
                }
                // A childless option stays legal: its nested sequence is simply empty.

                Execute(connection, transaction,
                    "INSERT INTO action_instance_choice_option (id, action_instance_id, ordinal, label, action_sequence_id) " +
                    "VALUES (@id, @instance, @ordinal, @label, @seq);",
                    Param("id", $"optn-{optionId}"), Param("instance", choiceInstanceId),
                    Param("ordinal", ordinal), Param("label", label), Param("seq", optionSequenceId));
            }
        }

        // ---- sessions -> macro graphs ----

        private static void InsertSessionGraphs(
            DbConnection connection, DbTransaction transaction,
            Dictionary<string, List<string>> placementsBySession)
        {
            var sessionIds = new List<string>();
            ReadAll(connection, transaction, "SELECT id FROM session ORDER BY id;", reader => sessionIds.Add(reader.GetString(0)));

            foreach (var sessionId in sessionIds)
            {
                placementsBySession.TryGetValue(sessionId, out var placements);
                InsertSessionGraph(connection, transaction, sessionId, placements ?? new List<string>());
            }
        }

        /// <summary>
        /// Start → ordered referenced phases → End. Each reference projects a socket
        /// per standard exit; Complete continues to the next placement (last → End),
        /// Fail goes straight to SessionEnd. Zero placements degenerate to Start→End.
        /// </summary>
        private static void InsertSessionGraph(DbConnection connection, DbTransaction transaction, string sessionId, List<string> placements)
        {
            var start = $"sn-{sessionId}-start";
            var end = $"sn-{sessionId}-end";

            InsertSessionNode(connection, transaction, start, sessionId, "Start");
            SessionNodeOutput(connection, transaction, $"{start}-out", start, "normal", 0, null, null);

            InsertSessionNode(connection, transaction, end, sessionId, "End");

            var edges = new List<(string SourceOutputId, string TargetNodeId)>
            {
                ($"{start}-out", placements.Count > 0 ? $"{RefNodeId(sessionId, 0)}" : end),
            };

            for (var i = 0; i < placements.Count; i++)
            {
                var refNode = RefNodeId(sessionId, i);
                var phaseId = placements[i];

                InsertSessionNode(connection, transaction, refNode, sessionId, "PhaseReference");
                Execute(connection, transaction,
                    "INSERT INTO session_node_phase (node_id, phase_id) VALUES (@node, @phase);",
                    Param("node", refNode), Param("phase", phaseId));

                SessionNodeOutput(connection, transaction, $"{refNode}-socket-complete", refNode, "phase_exit", 0,
                    phaseExitId: ExitId(phaseId, complete: true),
                    sessionGotoInstanceId: null);
                SessionNodeOutput(connection, transaction, $"{refNode}-socket-fail", refNode, "phase_exit", 1,
                    phaseExitId: ExitId(phaseId, complete: false),
                    sessionGotoInstanceId: null);

                edges.Add(($"{refNode}-socket-complete", i == placements.Count - 1 ? end : RefNodeId(sessionId, i + 1)));
                edges.Add(($"{refNode}-socket-fail", end));
            }

            for (var e = 0; e < edges.Count; e++)
            {
                Execute(connection, transaction,
                    "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) " +
                    "VALUES (@id, @session, @source, @target);",
                    Param("id", $"se-{sessionId}-{e}"), Param("session", sessionId),
                    Param("source", edges[e].SourceOutputId), Param("target", edges[e].TargetNodeId));
            }
        }

        private static string RefNodeId(string sessionId, int index)
        {
            return $"sn-{sessionId}-ref{index}";
        }

        private static string ExitId(string phaseId, bool complete)
        {
            return complete ? $"px-{phaseId}-complete" : $"px-{phaseId}-fail";
        }

        private static void InsertSessionNode(DbConnection connection, DbTransaction transaction, string nodeId, string sessionId, string nodeType)
        {
            Execute(connection, transaction,
                "INSERT INTO session_graph_node (id, session_id, node_type) VALUES (@id, @session, @type);",
                Param("id", nodeId), Param("session", sessionId), Param("type", nodeType));
        }

        private static void SessionNodeOutput(
            DbConnection connection, DbTransaction transaction,
            string outputId, string nodeId, string kind, int ordinal,
            string phaseExitId, string sessionGotoInstanceId)
        {
            Execute(connection, transaction,
                "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) " +
                "VALUES (@id, @node, @kind, @ordinal, NULL, @exit, @goto);",
                Param("id", outputId), Param("node", nodeId), Param("kind", kind), Param("ordinal", ordinal),
                Param("exit", (object)phaseExitId ?? DBNull.Value),
                Param("goto", (object)sessionGotoInstanceId ?? DBNull.Value));
        }

        // ---- shared low-level helpers ----

        private static (string Name, object Value) Param(string name, object value)
        {
            return (name, value);
        }

        private static void InsertInstanceRow(DbConnection connection, DbTransaction transaction, string instanceId, string sequenceId, int ordinal, string actionType, bool blocking = true)
        {
            Execute(connection, transaction,
                "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                "VALUES (@id, @seq, @ordinal, @type, @blocking);",
                Param("id", instanceId), Param("seq", sequenceId), Param("ordinal", ordinal),
                Param("type", actionType), Param("blocking", blocking ? 1 : 0));
        }

        private static void Execute(DbConnection connection, DbTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                Bind(command, parameters);
                command.ExecuteNonQuery();
            }
        }

        private static void ReadAll(DbConnection connection, DbTransaction transaction, string sql, Action<DbDataReader> visit, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                Bind(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        visit(reader);
                    }
                }
            }
        }

        private static void ReadOne(DbConnection connection, DbTransaction transaction, string sql, Action<DbDataReader> visit, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                Bind(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        visit(reader);
                    }
                }
            }
        }

        private static void Bind(DbCommand command, (string Name, object Value)[] parameters)
        {
            foreach (var parameter in parameters)
            {
                var dbParameter = command.CreateParameter();
                dbParameter.ParameterName = parameter.Name;
                dbParameter.Value = parameter.Value ?? DBNull.Value;
                command.Parameters.Add(dbParameter);
            }
        }
    }
}
