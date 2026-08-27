using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Reconstructs a complete portable GameContentDefinition from a SQLite v2
    /// core database (schema per Docs/GraphWorkbench/03-schema-v2-design.md).
    ///
    /// Reads ONLY v2 structures: session types, temperature definitions,
    /// session/phase graphs with typed nodes, output sockets and edges, owned
    /// action sequences/instances (+ explicit subtype rows), cards with their
    /// owned sequence, resources, and the deck. Legacy PhaseSlot/top-level-action
    /// rows are ignored entirely.
    ///
    /// Fail-loud rules: unknown node types / port kinds / instance types, missing
    /// subtype rows, unwired socket payloads, and flow-control instances marked
    /// nonblocking all throw immediately. All ordered queries ORDER BY ordinal;
    /// IDs are preserved verbatim; sequences referenced by several owners are
    /// loaded once and shared by reference exactly as stored.
    /// </summary>
    public static class GameContentSnapshotLoader
    {
        public static GameContentDefinition Load(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            ConnectionInitializer.Initialize(connection);
            CoreMigrator.EnsureSchema(connection);

            var content = new GameContentDefinition();
            var sequences = new SequenceCache(connection);

            content.SessionTypes.AddRange(LoadSessionTypes(connection));
            content.Temperatures.AddRange(LoadTemperatures(connection));
            content.Resources.AddRange(LoadResources(connection));
            content.Deck = LoadDeck(connection);
            content.Cards.AddRange(LoadCards(connection, sequences));
            content.Phases.AddRange(LoadPhases(connection, sequences));
            content.Sessions.AddRange(LoadSessions(connection, sequences));

            return content;
        }

        // ---- simple entities ----

        private static List<SessionTypeDefinition> LoadSessionTypes(DbConnection connection)
        {
            var types = new List<SessionTypeDefinition>();
            QueryAll(connection, "SELECT id, title FROM session_type ORDER BY id;",
                reader => types.Add(new SessionTypeDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                }));
            return types;
        }

        private static List<TemperatureDefinition> LoadTemperatures(DbConnection connection)
        {
            var temperatures = new List<TemperatureDefinition>();
            QueryAll(connection, "SELECT id, title, min_value, max_value, default_value FROM temperature_definition ORDER BY id;",
                reader => temperatures.Add(new TemperatureDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    MinValue = Convert.ToSingle(reader.GetValue(2)),
                    MaxValue = Convert.ToSingle(reader.GetValue(3)),
                    DefaultValue = Convert.ToSingle(reader.GetValue(4)),
                }));
            return temperatures;
        }

        private static List<ResourceDefinition> LoadResources(DbConnection connection)
        {
            var resources = new List<ResourceDefinition>();
            QueryAll(connection, "SELECT id, kind, name FROM resource ORDER BY id;",
                reader => resources.Add(new ResourceDefinition
                {
                    Id = reader.GetString(0),
                    Kind = reader.GetString(1),
                    Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                }));
            return resources;
        }

        private static CardDeckDefinition LoadDeck(DbConnection connection)
        {
            var deckId = "";
            var title = "";
            QueryOne(connection, "SELECT id, title FROM card_deck ORDER BY id LIMIT 1;",
                reader =>
                {
                    deckId = reader.GetString(0);
                    title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                });

            if (string.IsNullOrEmpty(deckId))
            {
                return new CardDeckDefinition();
            }

            var deck = new CardDeckDefinition { Id = deckId, Title = title };
            QueryAll(connection, "SELECT card_id FROM card_deck_card WHERE deck_id = @deck ORDER BY ordinal;",
                reader => deck.CardIds.Add(reader.GetString(0)),
                Param("deck", deckId));
            return deck;
        }

        // ---- cards ----

        private static List<CardDefinition> LoadCards(DbConnection connection, SequenceCache sequences)
        {
            var cards = new List<CardDefinition>();

            var ids = new List<string>();
            QueryAll(connection, "SELECT id FROM card ORDER BY id;", reader => ids.Add(reader.GetString(0)));

            foreach (var id in ids)
            {
                string title = "";
                string sequenceId = "";
                QueryOne(connection, "SELECT title, action_sequence_id FROM card WHERE id = @id;",
                    reader =>
                    {
                        title = reader.GetString(0);
                        sequenceId = reader.IsDBNull(1) ? null : reader.GetString(1);
                    },
                    Param("id", id));

                if (string.IsNullOrEmpty(sequenceId))
                {
                    throw new InvalidOperationException($"Loader: card '{id}' has no owned action_sequence_id.");
                }

                var card = new CardDefinition { Id = id, Title = title };
                card.Tags.AddRange(OrderedTagNames(connection,
                    "SELECT t.name FROM card_tag ct JOIN tag t ON t.id = ct.tag_id WHERE ct.card_id = @id ORDER BY ct.ordinal;",
                    Param("id", id)));
                card.Sequence = sequences.Load(sequenceId);
                cards.Add(card);
            }

            return cards;
        }

        // ---- phases ----

        private static List<PhaseDefinition> LoadPhases(DbConnection connection, SequenceCache sequences)
        {
            var phases = new List<PhaseDefinition>();

            var ids = new List<string>();
            QueryAll(connection, "SELECT id FROM phase ORDER BY id;", reader => ids.Add(reader.GetString(0)));

            foreach (var id in ids)
            {
                string title = "";
                QueryOne(connection, "SELECT title FROM phase WHERE id = @id;",
                    reader => title = reader.GetString(0), Param("id", id));

                var phase = new PhaseDefinition { Id = id, Title = title };
                phase.MustIncludeTags.AddRange(OrderedTagNames(connection,
                    "SELECT t.name FROM phase_required_tag prt JOIN tag t ON t.id = prt.tag_id WHERE prt.phase_id = @id ORDER BY prt.ordinal;",
                    Param("id", id)));
                phase.MustExcludeTags.AddRange(OrderedTagNames(connection,
                    "SELECT t.name FROM phase_excluded_tag pet JOIN tag t ON t.id = pet.tag_id WHERE pet.phase_id = @id ORDER BY pet.ordinal;",
                    Param("id", id)));

                QueryAll(connection, "SELECT id, name FROM phase_exit WHERE phase_id = @phase ORDER BY ordinal;",
                    reader => phase.Exits.Add(new PhaseExitDefinition
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                    }),
                    Param("phase", id));

                var graph = LoadGraph(
                    connection,
                    nodeTable: "phase_graph_node",
                    parentColumn: "phase_id",
                    parentId: id,
                    outputTable: "phase_node_output",
                    edgeTable: "phase_graph_edge",
                    edgeParentColumn: "phase_id",
                    nodeFactory: nodeId => BuildPhaseNode(connection, nodeId, sequences),
                    portKindError: kind => $"Loader: unknown phase port_kind '{kind}'.");
                phase.Graph.Nodes = graph.Nodes;
                phase.Graph.Edges = graph.Edges;

                phases.Add(phase);
            }

            return phases;
        }

        private static GraphNodeDefinition BuildPhaseNode(DbConnection connection, string nodeId, SequenceCache sequences)
        {
            // Variable check?
            string sourceKind = null;
            string variableKey = null;
            string compareOperator = null;
            double compareValue = 0;
            QueryOne(connection,
                "SELECT source_kind, variable_key, compare_operator, compare_value FROM phase_node_variable_check WHERE node_id = @n;",
                reader =>
                {
                    sourceKind = reader.GetString(0);
                    variableKey = reader.IsDBNull(1) ? null : reader.GetString(1);
                    compareOperator = reader.GetString(2);
                    compareValue = reader.GetDouble(3);
                },
                Param("n", nodeId));

            if (sourceKind != null)
            {
                return new VariableCheckNodeDefinition
                {
                    SourceKind = ParseSourceKind(sourceKind, nodeId),
                    VariableKey = variableKey ?? "",
                    Operator = ParseCompareOperator(compareOperator, nodeId),
                    CompareValue = Convert.ToSingle(compareValue),
                };
            }

            // Action node?
            string actionSequenceId = null;
            QueryOne(connection, "SELECT action_sequence_id FROM phase_node_action WHERE node_id = @n;",
                reader => actionSequenceId = reader.IsDBNull(0) ? null : reader.GetString(0),
                Param("n", nodeId));

            if (actionSequenceId != null)
            {
                return new ActionNodeDefinition
                {
                    Sequence = sequences.Load(actionSequenceId),
                };
            }

            // Decision?
            string prompt = null;
            QueryOne(connection, "SELECT prompt FROM phase_node_decision WHERE node_id = @n;",
                reader => prompt = reader.IsDBNull(0) ? null : reader.GetString(0),
                Param("n", nodeId));

            if (prompt != null)
            {
                return new PhaseDecisionNodeDefinition
                {
                    Prompt = prompt,
                    Options = LoadPhaseDecisionOptions(connection, nodeId, sequences),
                };
            }

            // Entry / CardExecutor / Return carry no subtype rows: distinguish by node_type.
            string nodeType = null;
            QueryOne(connection, "SELECT node_type FROM phase_graph_node WHERE id = @n;",
                reader => nodeType = reader.GetString(0), Param("n", nodeId));

            switch (nodeType)
            {
                case GraphNodeTypeNames.PhaseEntry: return new PhaseEntryNodeDefinition();
                case GraphNodeTypeNames.CardExecutor: return new CardExecutorNodeDefinition();
                case GraphNodeTypeNames.Return: return new ReturnNodeDefinition();
                case GraphNodeTypeNames.Action:
                case GraphNodeTypeNames.VariableCheck:
                case GraphNodeTypeNames.Decision:
                    throw new InvalidOperationException(
                        $"Loader: phase node '{nodeId}' declares type '{nodeType}' but its subtype row is missing.");
                default:
                    throw new InvalidOperationException($"Loader: unknown phase node_type '{nodeType}'.");
            }
        }

        private static List<PhaseDecisionOptionDefinition> LoadPhaseDecisionOptions(DbConnection connection, string nodeId, SequenceCache sequences)
        {
            var options = new List<PhaseDecisionOptionDefinition>();
            QueryAll(connection,
                "SELECT id, label, action_sequence_id FROM phase_decision_option WHERE node_id = @n ORDER BY ordinal;",
                reader => options.Add(new PhaseDecisionOptionDefinition
                {
                    Id = reader.GetString(0),
                    Label = reader.GetString(1),
                    Sequence = sequences.Load(reader.GetString(2)),
                }),
                Param("n", nodeId));
            return options;
        }

        // ---- sessions ----

        private static List<SessionDefinition> LoadSessions(DbConnection connection, SequenceCache sequences)
        {
            var sessions = new List<SessionDefinition>();

            var ids = new List<string>();
            QueryAll(connection, "SELECT id FROM session ORDER BY id;", reader => ids.Add(reader.GetString(0)));

            foreach (var id in ids)
            {
                string title = "";
                string sessionTypeId = "";
                QueryOne(connection, "SELECT title, session_type_id FROM session WHERE id = @id;",
                    reader =>
                    {
                        title = reader.GetString(0);
                        sessionTypeId = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    },
                    Param("id", id));

                if (string.IsNullOrEmpty(sessionTypeId))
                {
                    throw new InvalidOperationException($"Loader: session '{id}' has no session_type_id.");
                }

                var session = new SessionDefinition
                {
                    Id = id,
                    Title = title,
                    SessionTypeId = sessionTypeId,
                };
                session.Tags.AddRange(OrderedTagNames(connection,
                    "SELECT t.name FROM session_tag st JOIN tag t ON t.id = st.tag_id WHERE st.session_id = @id ORDER BY st.ordinal;",
                    Param("id", id)));

                var graph = LoadGraph(
                    connection,
                    nodeTable: "session_graph_node",
                    parentColumn: "session_id",
                    parentId: id,
                    outputTable: "session_node_output",
                    edgeTable: "session_graph_edge",
                    edgeParentColumn: "session_id",
                    nodeFactory: nodeId => BuildSessionNode(connection, nodeId, sequences),
                    portKindError: kind => $"Loader: unknown session port_kind '{kind}'.");
                foreach (var node in graph.Nodes)
                {
                    session.Graph.Nodes.Add((SessionGraphNodeDefinition)node);
                }
                session.Graph.Edges = graph.Edges;

                sessions.Add(session);
            }

            return sessions;
        }

        private static GraphNodeDefinition BuildSessionNode(DbConnection connection, string nodeId, SequenceCache sequences)
        {
            string phaseId = null;
            QueryOne(connection, "SELECT phase_id FROM session_node_phase WHERE node_id = @n;",
                reader => phaseId = reader.GetString(0),
                Param("n", nodeId));
            if (phaseId != null)
            {
                return new PhaseReferenceNodeDefinition { PhaseId = phaseId };
            }

            string prompt = null;
            QueryOne(connection, "SELECT prompt FROM session_node_decision WHERE node_id = @n;",
                reader => prompt = reader.IsDBNull(0) ? null : reader.GetString(0),
                Param("n", nodeId));
            if (prompt != null)
            {
                var options = new List<SessionDecisionOptionDefinition>();
                QueryAll(connection,
                    "SELECT id, label, action_sequence_id FROM session_decision_option WHERE node_id = @n ORDER BY ordinal;",
                    reader => options.Add(new SessionDecisionOptionDefinition
                    {
                        Id = reader.GetString(0),
                        Label = reader.GetString(1),
                        Sequence = sequences.Load(reader.GetString(2)),
                    }),
                    Param("n", nodeId));
                return new SessionDecisionNodeDefinition { Prompt = prompt, Options = options };
            }

            string nodeType = null;
            QueryOne(connection, "SELECT node_type FROM session_graph_node WHERE id = @n;",
                reader => nodeType = reader.GetString(0), Param("n", nodeId));

            switch (nodeType)
            {
                case GraphNodeTypeNames.SessionStart: return new SessionStartNodeDefinition();
                case GraphNodeTypeNames.SessionEnd: return new SessionEndNodeDefinition();
                case GraphNodeTypeNames.SessionPhaseReference:
                    throw new InvalidOperationException(
                        $"Loader: session node '{nodeId}' is a PhaseReference but has no session_node_phase row.");
                case GraphNodeTypeNames.SessionDecision:
                    throw new InvalidOperationException(
                        $"Loader: session node '{nodeId}' is a Decision but has no session_node_decision row.");
                default:
                    throw new InvalidOperationException($"Loader: unknown session node_type '{nodeType}'.");
            }
        }

        // ---- shared graph reading ----

        private sealed class LoadedGraph
        {
            public readonly List<GraphNodeDefinition> Nodes = new List<GraphNodeDefinition>();
            public readonly List<GraphEdgeDefinition> Edges = new List<GraphEdgeDefinition>();
        }

        private static LoadedGraph LoadGraph(
            DbConnection connection,
            string nodeTable, string parentColumn, string parentId,
            string outputTable, string edgeTable, string edgeParentColumn,
            Func<string, GraphNodeDefinition> nodeFactory,
            Func<string, string> portKindError)
        {
            var result = new LoadedGraph();

            var nodesById = new Dictionary<string, GraphNodeDefinition>();

            // Rows ordered by their own id keeps load order deterministic without a per-node ordinal column.
            QueryAll(connection,
                $"SELECT id, node_type FROM {nodeTable} WHERE {parentColumn} = @parent ORDER BY id;",
                reader =>
                {
                    var nodeId = reader.GetString(0);
                    var definition = nodeFactory(nodeId);
                    if (string.IsNullOrEmpty(definition.Id))
                    {
                        definition.Id = nodeId;
                    }
                    nodesById[nodeId] = definition;
                    result.Nodes.Add(definition);
                },
                Param("parent", parentId));

            // Attach output sockets to their owning node definitions.
            foreach (var node in result.Nodes) node.Outputs.Clear();

            bool isSessionLevel = outputTable == "session_node_output";
            QueryAll(connection,
                $"SELECT o.id, o.node_id, o.port_kind" +
                (isSessionLevel ? ", o.label, o.phase_exit_id, o.session_goto_action_instance_id" : "") +
                $" FROM {outputTable} o WHERE o.node_id IN (SELECT n.id FROM {nodeTable} n WHERE n.{parentColumn} = @parent)" +
                " ORDER BY o.node_id, o.ordinal;",
                reader =>
                {
                    var owner = reader.GetString(1);
                    if (!nodesById.TryGetValue(owner, out var definition))
                    {
                        throw new InvalidOperationException($"Loader: output '{reader.GetString(0)}' references unknown node '{owner}'.");
                    }

                    var output = new GraphOutputDefinition
                    {
                        Id = reader.GetString(0),
                        Kind = ParsePortKind(reader.GetString(2)),
                    };

                    if (isSessionLevel)
                    {
                        output.Label = reader.IsDBNull(3) ? "" : reader.GetString(3);
                        output.PhaseExitId = reader.IsDBNull(4) ? "" : reader.GetString(4);
                        output.SessionGotoActionInstanceId = reader.IsDBNull(5) ? "" : reader.GetString(5);

                        if (output.Kind == GraphPortKind.PhaseExit && string.IsNullOrEmpty(output.PhaseExitId))
                        {
                            throw new InvalidOperationException($"Loader: phase_exit socket '{output.Id}' has no mapped exit.");
                        }
                        if (output.Kind == GraphPortKind.SessionGoto && string.IsNullOrEmpty(output.SessionGotoActionInstanceId))
                        {
                            throw new InvalidOperationException($"Loader: session_goto socket '{output.Id}' has no owning instance.");
                        }
                    }

                    definition.Outputs.Add(output);
                },
                Param("parent", parentId));

            // Edges.
            QueryAll(connection,
                $"SELECT e.id, e.source_port_id, e.target_node_id FROM {edgeTable} e WHERE e.{edgeParentColumn} = @parent ORDER BY e.id;",
                reader => result.Edges.Add(new GraphEdgeDefinition
                {
                    Id = reader.GetString(0),
                    SourceOutputId = reader.GetString(1),
                    TargetNodeId = reader.GetString(2),
                }),
                Param("parent", parentId));

            return result;
        }

        // ---- tags/helpers ----

        private static List<string> OrderedTagNames(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            var names = new List<string>();
            QueryAll(connection, sql, reader => names.Add(reader.GetString(0)), parameters);
            return names;
        }

        private static VariableSourceKind ParseSourceKind(string value, string nodeId)
        {
            switch (value)
            {
                case "progress": return VariableSourceKind.PhaseProgress;
                case "temperature": return VariableSourceKind.Temperature;
                case "stat": return VariableSourceKind.Stat;
                default:
                    throw new InvalidOperationException($"Loader: unknown variable source_kind '{value}' on node '{nodeId}'.");
            }
        }

        private static VariableCompareOperator ParseCompareOperator(string value, string nodeId)
        {
            switch (value)
            {
                case "<": return VariableCompareOperator.LessThan;
                case "<=": return VariableCompareOperator.LessThanOrEqual;
                case "==": return VariableCompareOperator.Equal;
                case "!=": return VariableCompareOperator.NotEqual;
                case ">=": return VariableCompareOperator.GreaterThanOrEqual;
                case ">": return VariableCompareOperator.GreaterThan;
                default:
                    throw new InvalidOperationException($"Loader: unknown compare_operator '{value}' on node '{nodeId}'.");
            }
        }

        private static GraphPortKind ParsePortKind(string value)
        {
            switch (value)
            {
                case "normal": return GraphPortKind.Normal;
                case "true": return GraphPortKind.True;
                case "false": return GraphPortKind.False;
                case "phase_exit": return GraphPortKind.PhaseExit;
                case "session_goto": return GraphPortKind.SessionGoto;
                default:
                    throw new InvalidOperationException($"Loader: unknown port_kind '{value}'.");
            }
        }

        // ---- raw ADO helpers ----

        private static (string Name, object Value) Param(string name, object value)
        {
            return (name, value);
        }

        private static void QueryAll(DbConnection connection, string sql, Action<DbDataReader> visit, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                Bind(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) visit(reader);
                }
            }
        }

        private static void QueryOne(DbConnection connection, string sql, Action<DbDataReader> visit, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                Bind(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read()) visit(reader);
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

        /// <summary>
        /// Loads owned action sequences (recursively, for nested choice-option
        /// sequences). Sequences are cached so two owners referencing one row see
        /// the same object graph — matching the storage model where an occurrence
        /// belongs to exactly one sequence but nested option sequences are distinct
        /// rows reachable only through their PromptChoice instance.
        /// </summary>
        private sealed class SequenceCache
        {
            private readonly DbConnection _connection;
            private readonly Dictionary<string, ActionSequenceDefinition> _cache = new Dictionary<string, ActionSequenceDefinition>();

            public SequenceCache(DbConnection connection)
            {
                _connection = connection;
            }

            public ActionSequenceDefinition Load(string sequenceId)
            {
                if (_cache.TryGetValue(sequenceId, out var cached))
                {
                    return cached;
                }

                var exists = false;
                QueryOne(_connection, "SELECT COUNT(*) FROM action_sequence WHERE id = @id;",
                    reader => exists = Convert.ToInt64(reader.GetValue(0)) > 0,
                    Param("id", sequenceId));
                if (!exists)
                {
                    throw new InvalidOperationException($"Loader: missing action_sequence '{sequenceId}'.");
                }

                var sequence = new ActionSequenceDefinition { Id = sequenceId };
                _cache[sequenceId] = sequence;

                var instanceRows = new List<(string Id, string Type, int Blocking)>();
                QueryAll(_connection,
                    "SELECT id, action_type, is_blocking FROM action_instance WHERE action_sequence_id = @seq ORDER BY ordinal;",
                    reader => instanceRows.Add((reader.GetString(0), reader.GetString(1), Convert.ToInt32(reader.GetValue(2)))),
                    Param("seq", sequenceId));

                foreach (var (instanceId, type, blockingInt) in instanceRows)
                {
                    var blocking = blockingInt == 1;
                    if (!blocking && ActionType.IsAlwaysBlocking(type))
                    {
                        throw new InvalidOperationException(
                            $"Loader: flow-control instance '{instanceId}' ({type}) must be blocking.");
                    }

                    sequence.Instances.Add(LoadInstance(sequenceId, instanceId, type, blocking));
                }

                return sequence;
            }

            private ActionInstanceDefinition LoadInstance(string sequenceId, string instanceId, string type, bool blocking)
            {
                switch (type)
                {
                    case ActionType.Debug:
                    {
                        string message = null;
                        object delaySeconds = DBNull.Value;
                        QueryOne(_connection, "SELECT message, delay_seconds FROM action_instance_debug WHERE action_instance_id = @i;",
                            reader =>
                            {
                                message = reader.IsDBNull(0) ? null : reader.GetString(0);
                                delaySeconds = reader.IsDBNull(1) ? (object)DBNull.Value : (object)reader.GetDouble(1);
                            },
                            Param("i", instanceId));
                        RequireSubtypeRow(message != null || delaySeconds != DBNull.Value, sequenceId, instanceId, type);
                        return new DebugInstanceDefinition
                        {
                            Id = instanceId,
                            IsBlocking = blocking,
                            Message = message ?? "",
                            DelaySeconds = delaySeconds == DBNull.Value ? 0f : Convert.ToSingle(delaySeconds),
                        };
                    }

                    case ActionType.StatIncrease:
                    {
                        string statKey = null;
                        double amount = 0;
                        QueryOne(_connection, "SELECT stat_key, amount FROM action_instance_stat_increase WHERE action_instance_id = @i;",
                            reader => { statKey = reader.GetString(0); amount = reader.GetDouble(1); },
                            Param("i", instanceId));
                        RequireSubtypeRow(statKey != null, sequenceId, instanceId, type);
                        return new StatIncreaseInstanceDefinition
                        {
                            Id = instanceId,
                            IsBlocking = blocking,
                            StatKey = statKey,
                            Amount = Convert.ToSingle(amount),
                        };
                    }

                    case ActionType.IncrementProgressV2:
                    {
                        double? amount = null;
                        QueryOne(_connection, "SELECT amount FROM action_instance_increment_progress WHERE action_instance_id = @i;",
                            reader => amount = reader.GetDouble(0),
                            Param("i", instanceId));
                        RequireSubtypeRow(amount.HasValue, sequenceId, instanceId, type);
                        return new IncrementProgressInstanceDefinition
                        {
                            Id = instanceId,
                            IsBlocking = blocking,
                            Amount = Convert.ToSingle(amount.Value),
                        };
                    }

                    case ActionType.ModifyTemperatureV2:
                    {
                        string temperatureId = null;
                        double amount = 0;
                        QueryOne(_connection, "SELECT temperature_id, amount FROM action_instance_modify_temperature WHERE action_instance_id = @i;",
                            reader => { temperatureId = reader.GetString(0); amount = reader.GetDouble(1); },
                            Param("i", instanceId));
                        RequireSubtypeRow(temperatureId != null, sequenceId, instanceId, type);
                        return new ModifyTemperatureInstanceDefinition
                        {
                            Id = instanceId,
                            IsBlocking = blocking,
                            TemperatureId = temperatureId,
                            Amount = Convert.ToSingle(amount),
                        };
                    }

                    case ActionType.Cutscene:
                    {
                        string resourceId = null;
                        QueryOne(_connection, "SELECT resource_id FROM action_instance_cutscene WHERE action_instance_id = @i;",
                            reader => resourceId = reader.GetString(0),
                            Param("i", instanceId));
                        RequireSubtypeRow(resourceId != null, sequenceId, instanceId, type);
                        return new CutsceneInstanceDefinition
                        {
                            Id = instanceId,
                            IsBlocking = blocking,
                            ResourceId = resourceId,
                        };
                    }

                    case ActionType.PromptChoiceV2:
                    {
                        string prompt = null;
                        QueryOne(_connection, "SELECT prompt FROM action_instance_prompt_choice WHERE action_instance_id = @i;",
                            reader => prompt = reader.GetString(0),
                            Param("i", instanceId));
                        RequireSubtypeRow(prompt != null, sequenceId, instanceId, type);

                        var choice = new PromptChoiceInstanceDefinition
                        {
                            Id = instanceId,
                            IsBlocking = blocking,
                            Prompt = prompt,
                        };
                        QueryAll(_connection,
                            "SELECT id, label, action_sequence_id FROM action_instance_choice_option WHERE action_instance_id = @i ORDER BY ordinal;",
                            reader => choice.Options.Add(new PromptChoiceOptionDefinition
                            {
                                Id = reader.GetString(0),
                                Label = reader.GetString(1),
                                Sequence = Load(reader.GetString(2)),
                            }),
                            Param("i", instanceId));
                        return choice;
                    }

                    case ActionType.PhaseGotoV2:
                    {
                        string exitId = null;
                        QueryOne(_connection, "SELECT phase_exit_id FROM action_instance_phase_goto WHERE action_instance_id = @i;",
                            reader => exitId = reader.GetString(0),
                            Param("i", instanceId));
                        RequireSubtypeRow(exitId != null, sequenceId, instanceId, type);
                        return new PhaseGotoInstanceDefinition
                        {
                            Id = instanceId,
                            IsBlocking = true,
                            PhaseExitId = exitId,
                        };
                    }

                    case ActionType.SessionGotoV2:
                    {
                        string label = null;
                        QueryOne(_connection, "SELECT label FROM action_instance_session_goto WHERE action_instance_id = @i;",
                            reader => label = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            Param("i", instanceId));
                        RequireSubtypeRow(label != null, sequenceId, instanceId, type);
                        return new SessionGotoInstanceDefinition
                        {
                            Id = instanceId,
                            IsBlocking = true,
                            Label = label,
                        };
                    }

                    case ActionType.ReturnV2:
                    case ActionType.EndSessionV2:
                    {
                        var table = type == ActionType.ReturnV2 ? "action_instance_return" : "action_instance_end_session";
                        var exists = false;
                        QueryOne(_connection, $"SELECT COUNT(*) FROM {table} WHERE action_instance_id = @i;",
                            reader => exists = Convert.ToInt64(reader.GetValue(0)) > 0,
                            Param("i", instanceId));
                        RequireSubtypeRow(exists, sequenceId, instanceId, type);

                        return type == ActionType.ReturnV2
                            ? (ActionInstanceDefinition)new ReturnInstanceDefinition { Id = instanceId, IsBlocking = true }
                            : new EndSessionInstanceDefinition { Id = instanceId, IsBlocking = true };
                    }

                    default:
                        throw new InvalidOperationException(
                            $"Loader: unknown action_instance type '{type}' (sequence '{sequenceId}', instance '{instanceId}').");
                }
            }

            private void RequireSubtypeRow(bool found, string sequenceId, string instanceId, string type)
            {
                if (!found)
                {
                    throw new InvalidOperationException(
                        $"Loader: instance '{instanceId}' (sequence '{sequenceId}') of type '{type}' has no subtype row.");
                }
            }
        }
    }
}
