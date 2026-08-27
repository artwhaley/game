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
                    InsertTags(connection, transaction, content);

                    foreach (var phase in content.Phases)
                    {
                        WritePhase(connection, transaction, phase);
                    }

                    foreach (var card in content.Cards)
                    {
                        WriteCard(connection, transaction, card);
                    }

                    WriteDeck(connection, transaction, content.Deck);

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
            // migration 2 pre-seeds (INSERT OR IGNORE) on every database, so the
            // initializer must be idempotent against those rows rather than
            // re-inserting them. Resources are ordinary content and also use
            // OR IGNORE so re-seeding a partial database is harmless.
            foreach (var sessionType in content.SessionTypes)
            {
                RequireId(sessionType?.Id, "SessionType");
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO session_type (id, title) VALUES (@id, @title);",
                    ("id", sessionType.Id), ("title", sessionType.Title));
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
                "INSERT INTO card (id, title, action_sequence_id) VALUES (@id, @title, @seq);",
                ("id", card.Id), ("title", card.Title), ("seq", card.Sequence.Id));
            ReplaceTags(connection, transaction, "card_tag", "card_id", card.Id, card.Tags);
        }

        private static void WriteDeck(DbConnection connection, DbTransaction transaction, CardDeckDefinition deck)
        {
            if (deck == null || string.IsNullOrEmpty(deck.Id)) return;

            Sql.Execute(connection, transaction,
                "INSERT INTO card_deck (id, title) VALUES (@id, @title);",
                ("id", deck.Id), ("title", (object)deck.Title ?? DBNull.Value));

            for (var i = 0; i < deck.CardIds.Count; i++)
            {
                var cardId = deck.CardIds[i];
                if (string.IsNullOrEmpty(cardId))
                {
                    throw new InvalidOperationException("Deck entry at index " + i + " is null/empty (dense lists have no null slots).");
                }

                Sql.Execute(connection, transaction,
                    "INSERT INTO card_deck_card (deck_id, ordinal, card_id) VALUES (@deck, @ordinal, @card);",
                    ("deck", deck.Id), ("ordinal", i), ("card", cardId));
            }
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

            ReplaceTags(connection, transaction, "phase_required_tag", "phase_id", phase.Id, phase.MustIncludeTags);
            ReplaceTags(connection, transaction, "phase_excluded_tag", "phase_id", phase.Id, phase.MustExcludeTags);

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

            Sql.Execute(connection, transaction,
                "INSERT INTO session (id, title, session_type_id) VALUES (@id, @title, @type);",
                ("id", session.Id), ("title", session.Title), ("type", session.SessionTypeId));
            ReplaceTags(connection, transaction, "session_tag", "session_id", session.Id, session.Tags);

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

        // ---- tags ----

        private static void InsertTags(DbConnection connection, DbTransaction transaction, GameContentDefinition content)
        {
            var collected = new List<string>();

            void Add(IEnumerable<string> source)
            {
                if (source == null) return;
                foreach (var name in source)
                {
                    if (!string.IsNullOrEmpty(name) && !collected.Contains(name))
                    {
                        collected.Add(name);
                    }
                }
            }

            foreach (var session in content.Sessions) Add(session?.Tags);
            foreach (var phase in content.Phases)
            {
                if (phase == null) continue;
                Add(phase.MustIncludeTags);
                Add(phase.MustExcludeTags);
            }
            foreach (var card in content.Cards) Add(card?.Tags);

            Sql.EnsureTags(connection, transaction, collected);
        }

        private static void ReplaceTags(DbConnection connection, DbTransaction transaction, string table, string parentColumn, string parentId, IReadOnlyList<string> tagNames)
        {
            Sql.Execute(connection, transaction,
                $"DELETE FROM {table} WHERE {parentColumn} = @parent;",
                ("parent", parentId));

            Sql.EnsureTags(connection, transaction, tagNames);

            for (var i = 0; i < tagNames.Count; i++)
            {
                if (string.IsNullOrEmpty(tagNames[i]))
                {
                    throw new InvalidOperationException($"{table}: null/empty tag at index {i} for '{parentId}'.");
                }

                Sql.Execute(connection, transaction,
                    $"INSERT INTO {table} ({parentColumn}, tag_id, ordinal) VALUES (@parent, @tag, @ordinal);",
                    ("parent", parentId), ("tag", tagNames[i]), ("ordinal", i));
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

        private static bool IsCoreContentEmpty(DbConnection connection)
        {
            // NOTE: session_type and temperature_definition are EXCLUDED: migration 2
            // guarantees their default seeds even on a blank database, so counting them
            // would make 'fresh' databases look non-empty forever.
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT (SELECT COUNT(*) FROM session) + (SELECT COUNT(*) FROM session_graph_node) + " +
                    "(SELECT COUNT(*) FROM phase) + (SELECT COUNT(*) FROM phase_graph_node) + " +
                    "(SELECT COUNT(*) FROM card) + (SELECT COUNT(*) FROM action_instance) + " +
                    "(SELECT COUNT(*) FROM action_sequence) + (SELECT COUNT(*) FROM action_instance_session_goto) + " +
                    "(SELECT COUNT(*) FROM resource) + (SELECT COUNT(*) FROM tag) + " +
                    "(SELECT COUNT(*) FROM card_deck);";
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
