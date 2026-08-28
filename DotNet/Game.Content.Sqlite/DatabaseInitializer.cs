using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// One-shot bootstrap: populates an EMPTY core database from a portable
    /// snapshot, preserving all stable IDs, in a single transaction.
    ///
    /// Writes ONLY v2 structures (schema migrations run first if needed). Refuses
    /// any database with existing core content — this is a seeding path, not the
    /// normal Save route. Host extension tables (unity_*, wpf_*) are never
    /// touched.
    /// </summary>
    public static class DatabaseInitializer
    {
        public static void InitializeEmptyDatabaseFromSnapshot(DbConnection connection, GameContentDefinition content)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (content == null) throw new ArgumentNullException(nameof(content));

            ConnectionInitializer.Initialize(connection);
            CoreMigrator.EnsureSchema(connection);

            if (!IsCoreContentEmpty(connection))
            {
                throw new InvalidOperationException(
                    "DatabaseInitializer: refusing to initialize a non-empty core database. " +
                    "InitializeEmptyDatabaseFromSnapshot is only for blank core content tables.");
            }

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    WriteReferenceEntities(connection, transaction, content);

                    foreach (var phase in content.Phases)
                    {
                        WritePhase(connection, transaction, phase);
                    }

                    foreach (var card in content.Cards)
                    {
                        WriteCard(connection, transaction, card);
                    }

                    foreach (var session in content.Sessions)
                    {
                        WriteSession(connection, transaction, session);
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

        private static void WriteReferenceEntities(DbConnection connection, DbTransaction transaction, GameContentDefinition content)
        {
            // session_type and temperature_definition are reference data that
            // migrations pre-seed (INSERT OR IGNORE) on every database, so the
            // initializer must be idempotent against those rows rather than
            // re-inserting them. Resources and the v5 catalogs are ordinary
            // content and also use OR IGNORE so re-seeding a partial database
            // is harmless.
            foreach (var sessionType in content.SessionTypes)
            {
                RequireId(sessionType?.Id, "SessionType");
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO session_type (id, title, sort_order) VALUES (@id, @title, @sort);",
                    ("id", sessionType.Id), ("title", sessionType.Title), ("sort", sessionType.SortOrder));
                for (var i = 0; i < sessionType.RequiredCapabilityIds.Count; i++)
                {
                    Sql.Execute(connection, transaction,
                        "INSERT OR IGNORE INTO session_type_required_smart_toy_capability (session_type_id, capability_id, ordinal) " +
                        "VALUES (@type, @cap, @ordinal);",
                        ("type", sessionType.Id), ("cap", sessionType.RequiredCapabilityIds[i]), ("ordinal", i));
                }
            }

            foreach (var temperature in content.Temperatures)
            {
                RequireId(temperature?.Id, "TemperatureDefinition");
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO temperature_definition (id, title, min_value, max_value, default_value) " +
                    "VALUES (@id, @title, @min, @max, @defaultValue);",
                    ("id", temperature.Id), ("title", temperature.Title),
                    ("min", (double)temperature.MinValue), ("max", (double)temperature.MaxValue),
                    ("defaultValue", (double)temperature.DefaultValue));
            }

            foreach (var tag in content.CardTagDefinitions)
            {
                RequireId(tag?.Id, "CardTagDefinition");
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO card_tag_definition (id, title, sort_order) VALUES (@id, @title, @sort);",
                    ("id", tag.Id), ("title", tag.Title), ("sort", tag.SortOrder));
            }

            foreach (var kink in content.KinkDefinitions)
            {
                RequireId(kink?.Id, "KinkDefinition");
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO kink_definition (id, title, description, sort_order) VALUES (@id, @title, @desc, @sort);",
                    ("id", kink.Id), ("title", kink.Title),
                    ("desc", (object)kink.Description ?? DBNull.Value), ("sort", kink.SortOrder));
            }

            foreach (var equipment in content.EquipmentDefinitions)
            {
                RequireId(equipment?.Id, "EquipmentDefinition");
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO equipment_definition (id, title, category, sort_order) VALUES (@id, @title, @cat, @sort);",
                    ("id", equipment.Id), ("title", equipment.Title),
                    ("cat", (object)equipment.Category ?? DBNull.Value), ("sort", equipment.SortOrder));
            }

            foreach (var capability in content.SmartToyCapabilityDefinitions)
            {
                RequireId(capability?.Id, "SmartToyCapabilityDefinition");
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO smart_toy_capability_definition (id, title, category, sort_order) VALUES (@id, @title, @cat, @sort);",
                    ("id", capability.Id), ("title", capability.Title),
                    ("cat", (object)capability.Category ?? DBNull.Value), ("sort", capability.SortOrder));
            }

            foreach (var resource in content.Resources)
            {
                RequireId(resource?.Id, "Resource");
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO resource (id, kind, name) VALUES (@id, @kind, @name);",
                    ("id", resource.Id), ("kind", resource.Kind), ("name", (object)resource.Name ?? DBNull.Value));
            }
        }

        // ---- cards ----

        private static void WriteCard(DbConnection connection, DbTransaction transaction, CardDefinition card)
        {
            if (card == null) return;
            RequireId(card.Id, "Card");
            if (card.Sequence == null || string.IsNullOrEmpty(card.Sequence.Id))
            {
                throw new InvalidOperationException($"Card '{card.Id}' has no owned action sequence id.");
            }

            // The sequence row must exist before the card row references it (FK).
            ActionSequenceWriter.Write(connection, transaction, card.Sequence);

            Sql.Execute(connection, transaction,
                "INSERT INTO card (id, title, body_text, action_sequence_id) VALUES (@id, @title, @body, @seq);",
                ("id", card.Id), ("title", card.Title), ("body", (object)card.BodyText ?? DBNull.Value), ("seq", card.Sequence.Id));

            ReplaceRelations(connection, transaction, "card_tag", "card_id", "tag_id", card.Id, card.CardTagIds);
            ReplaceRelations(connection, transaction, "card_kink", "card_id", "kink_id", card.Id, card.KinkIds);
            ReplaceRelations(connection, transaction, "card_required_equipment", "card_id", "equipment_id", card.Id, card.RequiredEquipmentIds);
            ReplaceRelations(connection, transaction, "card_required_smart_toy_capability", "card_id", "capability_id", card.Id, card.RequiredCapabilityIds);
        }

        // ---- phases ----

        private static void WritePhase(DbConnection connection, DbTransaction transaction, PhaseDefinition phase)
        {
            if (phase == null) return;
            RequireId(phase.Id, "Phase");
            if (phase.Graph == null) throw new InvalidOperationException($"Phase '{phase.Id}' has no graph.");
            RequireSingularEntryNode(phase);

            // Legacy min/max columns are dead concepts; they stay zero-filled.
            Sql.Execute(connection, transaction,
                "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, @title, 0, 0);",
                ("id", phase.Id), ("title", phase.Title));

            for (var i = 0; i < phase.Exits.Count; i++)
            {
                var exit = phase.Exits[i];
                RequireId(exit?.Id, $"Exit {i} on phase '{phase.Id}'");
                Sql.Execute(connection, transaction,
                    "INSERT INTO phase_exit (id, phase_id, ordinal, name) VALUES (@id, @phase, @ordinal, @name);",
                    ("id", exit.Id), ("phase", phase.Id), ("ordinal", i), ("name", exit.Name));
            }

            ReplaceRelations(connection, transaction, "phase_card_all_tag", "phase_id", "tag_id", phase.Id, phase.MustHaveAllCardTags);
            ReplaceRelations(connection, transaction, "phase_card_any_tag", "phase_id", "tag_id", phase.Id, phase.MustHaveAnyCardTags);

            foreach (var node in phase.Graph.Nodes)
            {
                WritePhaseNode(connection, transaction, phase.Id, node);
            }

            foreach (var edge in phase.Graph.Edges)
            {
                RequireId(edge?.Id, $"Edge on phase '{phase.Id}'");
                Sql.Execute(connection, transaction,
                    "INSERT INTO phase_graph_edge (id, phase_id, source_port_id, target_node_id) VALUES (@id, @phase, @source, @target);",
                    ("id", edge.Id), ("phase", phase.Id), ("source", edge.SourceOutputId), ("target", edge.TargetNodeId));
            }
        }

        /// <summary>Shared with PhaseGraphRepository so node persistence has exactly one implementation.</summary>
        internal static void WritePhaseNode(DbConnection connection, DbTransaction transaction, string phaseId, GraphNodeDefinition node)
        {
            if (node == null) throw new InvalidOperationException($"A null node row exists inside phase '{phaseId}'.");
            RequireId(node.Id, $"Node on phase '{phaseId}'");

            var nodeType = NodeTypeNameForPhase(node);
            if (nodeType == null)
            {
                throw new InvalidOperationException($"Unsupported node '{node.GetType().Name}' inside phase '{phaseId}'.");
            }

            Sql.Execute(connection, transaction,
                "INSERT INTO phase_graph_node (id, phase_id, node_type) VALUES (@id, @phase, @type);",
                ("id", node.Id), ("phase", phaseId), ("type", nodeType));

            switch (node)
            {
                case VariableCheckNodeDefinition check:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO phase_node_variable_check (node_id, source_kind, variable_key, compare_operator, compare_value) " +
                        "VALUES (@node, @kind, @key, @op, @value);",
                        ("node", node.Id),
                        ("kind", SourceKindName(check.SourceKind)),
                        ("key", string.IsNullOrEmpty(check.VariableKey) ? DBNull.Value : (object)check.VariableKey),
                        ("op", OperatorName(check.Operator)),
                        ("value", (double)check.CompareValue));
                    break;

                case ActionNodeDefinition action:
                    RequireSequence(action.Sequence, $"action node '{node.Id}'");
                    // Sequence first: phase_node_action references action_sequence (FK).
                    ActionSequenceWriter.Write(connection, transaction, action.Sequence);
                    Sql.Execute(connection, transaction,
                        "INSERT INTO phase_node_action (node_id, action_sequence_id) VALUES (@node, @seq);",
                        ("node", node.Id), ("seq", action.Sequence.Id));
                    break;

                case PhaseDecisionNodeDefinition decision:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO phase_node_decision (node_id, prompt) VALUES (@node, @prompt);",
                        ("node", node.Id), ("prompt", decision.Prompt));

                    for (var i = 0; i < decision.Options.Count; i++)
                    {
                        var option = decision.Options[i];
                        RequireId(option?.Id, $"Decision option {i} on node '{node.Id}'");
                        RequireSequence(option.Sequence, $"decision option '{option.Id}'");

                        // Sequence first: phase_decision_option references action_sequence (FK).
                        ActionSequenceWriter.Write(connection, transaction, option.Sequence);
                        Sql.Execute(connection, transaction,
                            "INSERT INTO phase_decision_option (id, node_id, ordinal, label, action_sequence_id) " +
                            "VALUES (@id, @node, @ordinal, @label, @seq);",
                            ("id", option.Id), ("node", node.Id), ("ordinal", i),
                            ("label", option.Label), ("seq", option.Sequence.Id));
                    }
                    break;
            }

            for (var o = 0; o < node.Outputs.Count; o++)
            {
                var output = node.Outputs[o];
                RequireId(output?.Id, $"Output socket {o} on node '{node.Id}'");
                Sql.Execute(connection, transaction,
                    "INSERT INTO phase_node_output (id, node_id, port_kind, ordinal, label) VALUES (@id, @node, @kind, @ordinal, NULL);",
                    ("id", output.Id), ("node", node.Id), ("kind", PortKindName(output.Kind)), ("ordinal", o));
            }
        }

        // ---- sessions ----

        private static void WriteSession(DbConnection connection, DbTransaction transaction, SessionDefinition session)
        {
            if (session == null) return;
            RequireId(session.Id, "Session");
            RequireId(session.SessionTypeId, $"SessionTypeId of session '{session.Id}'");
            if (session.Graph == null) throw new InvalidOperationException($"Session '{session.Id}' has no graph.");
            RequireSingularStartNode(session);

            Sql.Execute(connection, transaction,
                "INSERT INTO session (id, title, session_type_id) VALUES (@id, @title, @type);",
                ("id", session.Id), ("title", session.Title), ("type", session.SessionTypeId));

            var weighting = session.CardWeighting ?? new SessionCardWeightingDefinition();
            Sql.Execute(connection, transaction,
                "INSERT OR IGNORE INTO session_card_weighting " +
                "(session_id, love_base, love_happiness_gain, like_base, like_happiness_gain, torture_base, torture_unhappiness_gain) " +
                "VALUES (@id, @lb, @lg, @kb, @kg, @tb, @tg);",
                ("id", session.Id),
                ("lb", (double)weighting.LoveBase), ("lg", (double)weighting.LoveHappinessGain),
                ("kb", (double)weighting.LikeBase), ("kg", (double)weighting.LikeHappinessGain),
                ("tb", (double)weighting.TortureBase), ("tg", (double)weighting.TortureUnhappinessGain));

            foreach (var node in session.Graph.Nodes)
            {
                WriteSessionNode(connection, transaction, session.Id, node);
            }

            foreach (var edge in session.Graph.Edges)
            {
                RequireId(edge?.Id, $"Edge on session '{session.Id}'");
                Sql.Execute(connection, transaction,
                    "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) VALUES (@id, @session, @source, @target);",
                    ("id", edge.Id), ("session", session.Id), ("source", edge.SourceOutputId), ("target", edge.TargetNodeId));
            }
        }

        /// <summary>Shared with SessionGraphRepository so node persistence has exactly one implementation.</summary>
        internal static void WriteSessionNode(DbConnection connection, DbTransaction transaction, string sessionId, GraphNodeDefinition node)
        {
            if (node == null) throw new InvalidOperationException($"A null node row exists inside session '{sessionId}'.");
            RequireId(node.Id, $"Node on session '{sessionId}'");

            var nodeType = NodeTypeNameForSession(node);
            if (nodeType == null)
            {
                throw new InvalidOperationException($"Unsupported node '{node.GetType().Name}' inside session '{sessionId}'.");
            }

            Sql.Execute(connection, transaction,
                "INSERT INTO session_graph_node (id, session_id, node_type) VALUES (@id, @session, @type);",
                ("id", node.Id), ("session", sessionId), ("type", nodeType));

            if (node is PhaseReferenceNodeDefinition phaseRef)
            {
                if (string.IsNullOrEmpty(phaseRef.PhaseId))
                {
                    throw new InvalidOperationException($"PhaseReference '{node.Id}' has no PhaseId.");
                }

                Sql.Execute(connection, transaction,
                    "INSERT INTO session_node_phase (node_id, phase_id) VALUES (@node, @phase);",
                    ("node", node.Id), ("phase", phaseRef.PhaseId));
            }
            else if (node is SessionDecisionNodeDefinition decision)
            {
                Sql.Execute(connection, transaction,
                    "INSERT INTO session_node_decision (node_id, prompt) VALUES (@node, @prompt);",
                    ("node", node.Id), ("prompt", decision.Prompt));

                for (var i = 0; i < decision.Options.Count; i++)
                {
                    var option = decision.Options[i];
                    RequireId(option?.Id, $"Session option {i} on node '{node.Id}'");
                    RequireSequence(option.Sequence, $"session decision option '{option.Id}'");

                    // Sequence first: session_decision_option references action_sequence (FK).
                    ActionSequenceWriter.Write(connection, transaction, option.Sequence);
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session_decision_option (id, node_id, ordinal, label, action_sequence_id) " +
                        "VALUES (@id, @node, @ordinal, @label, @seq);",
                        ("id", option.Id), ("node", node.Id), ("ordinal", i),
                        ("label", option.Label), ("seq", option.Sequence.Id));
                }
            }

            for (var o = 0; o < node.Outputs.Count; o++)
            {
                var output = node.Outputs[o];
                RequireId(output?.Id, $"Output socket {o} on node '{node.Id}'");
                ValidateSessionSocketConsistency(sessionId, output);

                Sql.Execute(connection, transaction,
                    "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) " +
                    "VALUES (@id, @node, @kind, @ordinal, @label, @exit, @goto);",
                    ("id", output.Id), ("node", node.Id), ("kind", PortKindName(output.Kind)), ("ordinal", o),
                    ("label", (object)output.Label ?? DBNull.Value),
                    ("exit", string.IsNullOrEmpty(output.PhaseExitId) ? DBNull.Value : (object)output.PhaseExitId),
                    ("goto", string.IsNullOrEmpty(output.SessionGotoActionInstanceId) ? DBNull.Value : (object)output.SessionGotoActionInstanceId));
            }
        }

        /// <summary>Discriminator consistency for session sockets, per schema contract.</summary>
        private static void ValidateSessionSocketConsistency(string sessionId, GraphOutputDefinition output)
        {
            if (output.Kind == GraphPortKind.PhaseExit && string.IsNullOrEmpty(output.PhaseExitId))
            {
                throw new InvalidOperationException(
                    $"phase_exit socket '{output.Id}' on session '{sessionId}' lacks its exit mapping.");
            }
            if (output.Kind != GraphPortKind.PhaseExit && !string.IsNullOrEmpty(output.PhaseExitId))
            {
                throw new InvalidOperationException(
                    $"socket '{output.Id}' on session '{sessionId}' carries an exit mapping but is not a phase_exit port.");
            }
            if (output.Kind == GraphPortKind.SessionGoto && string.IsNullOrEmpty(output.SessionGotoActionInstanceId))
            {
                throw new InvalidOperationException(
                    $"session_goto socket '{output.Id}' on session '{sessionId}' lacks its owning instance.");
            }
            if (output.Kind != GraphPortKind.SessionGoto && !string.IsNullOrEmpty(output.SessionGotoActionInstanceId))
            {
                throw new InvalidOperationException(
                    $"socket '{output.Id}' on session '{sessionId}' references a goto instance but is not a session_goto port.");
            }
        }

        // ---- relations ----

        private static void ReplaceRelations(DbConnection connection, DbTransaction transaction,
            string table, string parentColumn, string relationColumn, string parentId, IReadOnlyList<string> relationIds)
        {
            Sql.Execute(connection, transaction,
                $"DELETE FROM {table} WHERE {parentColumn} = @parent;",
                ("parent", parentId));

            for (var i = 0; i < relationIds.Count; i++)
            {
                if (string.IsNullOrEmpty(relationIds[i]))
                {
                    throw new InvalidOperationException($"{table}: null/empty id at index {i} for '{parentId}'.");
                }

                Sql.Execute(connection, transaction,
                    $"INSERT INTO {table} ({parentColumn}, {relationColumn}, ordinal) VALUES (@parent, @rel, @ordinal);",
                    ("parent", parentId), ("rel", relationIds[i]), ("ordinal", i));
            }
        }

        // ---- validation / naming helpers ----

        private static void RequireSequence(ActionSequenceDefinition sequence, string what)
        {
            if (sequence == null || string.IsNullOrEmpty(sequence.Id))
            {
                throw new InvalidOperationException($"{what} has no sequence id.");
            }
        }

        private static void RequireId(string id, string what)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException(what + " has an empty id.");
            }
        }

        private static void RequireSingularEntryNode(PhaseDefinition phase)
        {
            var entries = 0;
            foreach (var node in phase.Graph.Nodes)
            {
                if (node is PhaseEntryNodeDefinition) entries++;
            }
            if (entries != 1)
            {
                throw new InvalidOperationException($"Phase '{phase.Id}' must have exactly one Entry node (found {entries}).");
            }
        }

        private static void RequireSingularStartNode(SessionDefinition session)
        {
            var starts = 0;
            foreach (var node in session.Graph.Nodes)
            {
                if (node is SessionStartNodeDefinition) starts++;
            }
            if (starts != 1)
            {
                throw new InvalidOperationException($"Session '{session.Id}' must have exactly one Start node (found {starts}).");
            }
        }

        private static bool IsCoreContentEmpty(DbConnection connection)
        {
            // NOTE: session_type and temperature_definition are EXCLUDED: migrations
            // guarantee their default seeds even on a blank database, so counting them
            // would make 'fresh' databases look non-empty forever.
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT (SELECT COUNT(*) FROM session) + (SELECT COUNT(*) FROM session_graph_node) + " +
                    "(SELECT COUNT(*) FROM phase) + (SELECT COUNT(*) FROM phase_graph_node) + " +
                    "(SELECT COUNT(*) FROM card) + (SELECT COUNT(*) FROM action_instance) + " +
                    "(SELECT COUNT(*) FROM action_sequence) + (SELECT COUNT(*) FROM action_instance_session_goto) + " +
                    "(SELECT COUNT(*) FROM resource) + (SELECT COUNT(*) FROM card_tag_definition) + " +
                    "(SELECT COUNT(*) FROM kink_definition) + (SELECT COUNT(*) FROM equipment_definition) + " +
                    "(SELECT COUNT(*) FROM smart_toy_capability_definition);";
                return Convert.ToInt64(command.ExecuteScalar()) == 0L;
            }
        }

        internal static string PortKindName(GraphPortKind kind)
        {
            switch (kind)
            {
                case GraphPortKind.Normal: return "normal";
                case GraphPortKind.True: return "true";
                case GraphPortKind.False: return "false";
                case GraphPortKind.PhaseExit: return "phase_exit";
                case GraphPortKind.SessionGoto: return "session_goto";
                default:
                    throw new InvalidOperationException($"Unknown port kind '{kind}'.");
            }
        }

        internal static string SourceKindName(VariableSourceKind kind)
        {
            switch (kind)
            {
                case VariableSourceKind.PhaseProgress: return "progress";
                case VariableSourceKind.Temperature: return "temperature";
                case VariableSourceKind.Stat: return "stat";
                default:
                    throw new InvalidOperationException($"Unknown variable source kind '{kind}'.");
            }
        }

        internal static string OperatorName(VariableCompareOperator op)
        {
            switch (op)
            {
                case VariableCompareOperator.LessThan: return "<";
                case VariableCompareOperator.LessThanOrEqual: return "<=";
                case VariableCompareOperator.Equal: return "==";
                case VariableCompareOperator.NotEqual: return "!=";
                case VariableCompareOperator.GreaterThanOrEqual: return ">=";
                case VariableCompareOperator.GreaterThan: return ">";
                default:
                    throw new InvalidOperationException($"Unknown compare operator '{op}'.");
            }
        }

        internal static string NodeTypeNameForPhase(GraphNodeDefinition node)
        {
            if (node is PhaseEntryNodeDefinition) return GraphNodeTypeNames.PhaseEntry;
            if (node is CardExecutorNodeDefinition) return GraphNodeTypeNames.CardExecutor;
            if (node is VariableCheckNodeDefinition) return GraphNodeTypeNames.VariableCheck;
            if (node is ActionNodeDefinition) return GraphNodeTypeNames.Action;
            if (node is PhaseDecisionNodeDefinition) return GraphNodeTypeNames.Decision;
            if (node is ReturnNodeDefinition) return GraphNodeTypeNames.Return;
            return null;
        }

        internal static string NodeTypeNameForSession(GraphNodeDefinition node)
        {
            if (node is SessionStartNodeDefinition) return GraphNodeTypeNames.SessionStart;
            if (node is PhaseReferenceNodeDefinition) return GraphNodeTypeNames.SessionPhaseReference;
            if (node is SessionDecisionNodeDefinition) return GraphNodeTypeNames.SessionDecision;
            if (node is SessionEndNodeDefinition) return GraphNodeTypeNames.SessionEnd;
            return null;
        }
    }
}
