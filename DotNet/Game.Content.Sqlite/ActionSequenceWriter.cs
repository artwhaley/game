using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Writes owned Action Sequences and their instances (recursively including
    /// nested PromptChoice-option sequences) inside a caller-managed transaction.
    /// Shared by DatabaseInitializer and the authoring repositories so instance
    /// persistence has exactly one implementation.
    ///
    /// Rules enforced here: ordinals are assigned from list order and must be
    /// unique per sequence (DB UNIQUE backs this up); flow-control types are
    /// always blocking regardless of the in-memory flag (fail loud on conflict);
    /// unknown subtype classes fail loudly instead of guessing.
    /// </summary>
    internal static class ActionSequenceWriter
    {
        public static void Write(DbConnection connection, DbTransaction transaction, ActionSequenceDefinition sequence)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            if (string.IsNullOrEmpty(sequence.Id)) throw new InvalidOperationException("Sequence has no id.");

            // A sequence row is an identity anchor shared by reference; re-writing
            // an existing row must NOT replace it (REPLACE would cascade-delete
            // referencing rows like phase_node_action). Idempotent insert keeps
            // shared sequences intact; instances below are still (re)asserted.
            Sql.Execute(connection, transaction,
                "INSERT INTO action_sequence (id) VALUES (@id) ON CONFLICT(id) DO NOTHING;",
                ("id", sequence.Id));

            var seenOrdinals = new HashSet<int>();
            for (var ordinal = 0; ordinal < sequence.Instances.Count; ordinal++)
            {
                if (!seenOrdinals.Add(ordinal))
                {
                    throw new InvalidOperationException($"Duplicate ordinal {ordinal} while writing sequence '{sequence.Id}'.");
                }

                WriteInstance(connection, transaction, sequence.Id, sequence.Instances[ordinal], ordinal);
            }
        }

        /// <summary>
        /// Applies a stable-ID diff to an existing owned sequence. Surviving
        /// action rows are updated in place; only IDs absent from the desired
        /// sequence are deleted. PromptChoice options and their nested
        /// sequences are diffed recursively by the same rule.
        /// </summary>
        public static void Sync(DbConnection connection, DbTransaction transaction, ActionSequenceDefinition sequence)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            if (string.IsNullOrEmpty(sequence.Id)) throw new InvalidOperationException("Sequence has no id.");

            EnsureSequence(connection, transaction, sequence.Id);
            var existing = LoadInstances(connection, transaction, sequence.Id);
            var desiredIds = new HashSet<string>();
            for (var i = 0; i < sequence.Instances.Count; i++)
            {
                var instance = sequence.Instances[i];
                if (instance == null) throw new InvalidOperationException($"Sequence '{sequence.Id}' contains a null instance.");
                if (string.IsNullOrEmpty(instance.Id)) throw new InvalidOperationException($"Sequence '{sequence.Id}' contains an instance with no id.");
                if (!desiredIds.Add(instance.Id)) throw new InvalidOperationException($"Sequence '{sequence.Id}' contains duplicate instance id '{instance.Id}'.");

                if (existing.TryGetValue(instance.Id, out var stored) &&
                    !string.Equals(stored.ActionType, InstanceTypeName(instance), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Action instance '{instance.Id}' changes type from '{stored.ActionType}' to '{InstanceTypeName(instance)}'. " +
                        "Give the replacement action a new stable id.");
                }
            }

            foreach (var stored in existing.Values)
            {
                if (!desiredIds.Contains(stored.Id))
                {
                    Sql.Execute(connection, transaction,
                        "DELETE FROM action_instance WHERE id = @id;", ("id", stored.Id));
                }
            }

            ShiftActionOrdinals(connection, transaction, sequence.Id);
            for (var ordinal = 0; ordinal < sequence.Instances.Count; ordinal++)
            {
                var instance = sequence.Instances[ordinal];
                if (existing.ContainsKey(instance.Id))
                {
                    UpdateExistingInstance(connection, transaction, sequence.Id, instance, ordinal);
                }
                else
                {
                    WriteInstance(connection, transaction, sequence.Id, instance, ordinal);
                }
            }
        }

        public static void Delete(DbConnection connection, DbTransaction transaction, string sequenceId)
        {
            Sql.Execute(connection, transaction,
                "DELETE FROM action_sequence WHERE id = @id;",
                ("id", sequenceId));
        }

        /// <summary>
        /// Removes all instances owned by a sequence, including recursively
        /// owned PromptChoice option sequences, while keeping the root sequence
        /// identity row. Used by semantic sequence replacement/undo.
        /// </summary>
        internal static void ClearContents(DbConnection connection, DbTransaction transaction, string sequenceId)
        {
            var childSequences = new List<string>();
            Sql.QueryAll(connection, transaction,
                "SELECT action_sequence_id FROM action_instance_choice_option " +
                "WHERE action_instance_id IN (SELECT id FROM action_instance WHERE action_sequence_id = @seq);",
                reader => childSequences.Add(reader.GetString(0)), ("seq", sequenceId));

            foreach (var childSequenceId in childSequences)
            {
                ClearContents(connection, transaction, childSequenceId);
                Delete(connection, transaction, childSequenceId);
            }

            Sql.Execute(connection, transaction,
                "DELETE FROM action_instance WHERE action_sequence_id = @seq;",
                ("seq", sequenceId));
        }

        internal static void WriteSingleAt(DbConnection connection, DbTransaction transaction,
            string sequenceId, ActionInstanceDefinition instance, int ordinal)
        {
            if (string.IsNullOrEmpty(sequenceId)) throw new InvalidOperationException("Sequence id required.");
            Sql.Execute(connection, transaction,
                "INSERT INTO action_sequence (id) VALUES (@id) ON CONFLICT(id) DO NOTHING;",
                ("id", sequenceId));
            WriteInstance(connection, transaction, sequenceId, instance, ordinal);
        }

        private sealed class StoredInstance
        {
            public string Id;
            public string ActionType;
        }

        private sealed class StoredOption
        {
            public string Id;
            public string SequenceId;
        }

        private static Dictionary<string, StoredInstance> LoadInstances(
            DbConnection connection, DbTransaction transaction, string sequenceId)
        {
            var result = new Dictionary<string, StoredInstance>();
            Sql.QueryAll(connection, transaction,
                "SELECT id, action_type FROM action_instance WHERE action_sequence_id = @seq;",
                reader =>
                {
                    var id = reader.GetString(0);
                    result[id] = new StoredInstance { Id = id, ActionType = reader.GetString(1) };
                }, ("seq", sequenceId));
            return result;
        }

        private static Dictionary<string, StoredOption> LoadOptions(
            DbConnection connection, DbTransaction transaction, string choiceInstanceId)
        {
            var result = new Dictionary<string, StoredOption>();
            Sql.QueryAll(connection, transaction,
                "SELECT id, action_sequence_id FROM action_instance_choice_option WHERE action_instance_id = @instance;",
                reader =>
                {
                    var id = reader.GetString(0);
                    result[id] = new StoredOption { Id = id, SequenceId = reader.GetString(1) };
                }, ("instance", choiceInstanceId));
            return result;
        }

        private static void EnsureSequence(DbConnection connection, DbTransaction transaction, string sequenceId)
        {
            Sql.Execute(connection, transaction,
                "INSERT INTO action_sequence (id) VALUES (@id) ON CONFLICT(id) DO NOTHING;",
                ("id", sequenceId));
        }

        private static void ShiftActionOrdinals(DbConnection connection, DbTransaction transaction, string sequenceId)
        {
            Sql.Execute(connection, transaction,
                "UPDATE action_instance SET ordinal = ordinal + 1000000 WHERE action_sequence_id = @seq;",
                ("seq", sequenceId));
        }

        private static void ShiftOptionOrdinals(DbConnection connection, DbTransaction transaction, string choiceInstanceId)
        {
            Sql.Execute(connection, transaction,
                "UPDATE action_instance_choice_option SET ordinal = ordinal + 1000000 WHERE action_instance_id = @instance;",
                ("instance", choiceInstanceId));
        }

        private static void UpdateExistingInstance(DbConnection connection, DbTransaction transaction,
            string sequenceId, ActionInstanceDefinition instance, int ordinal)
        {
            var type = InstanceTypeName(instance);
            if (!instance.IsBlocking && ActionType.IsAlwaysBlocking(type))
            {
                throw new InvalidOperationException(
                    $"Flow-control instance '{instance.Id}' ({type}) cannot be nonblocking.");
            }

            Sql.Execute(connection, transaction,
                "UPDATE action_instance SET ordinal = @ordinal, is_blocking = @blocking WHERE id = @id AND action_sequence_id = @seq;",
                ("ordinal", ordinal), ("blocking", instance.IsBlocking ? 1 : 0),
                ("id", instance.Id), ("seq", sequenceId));

            switch (instance)
            {
                case DebugInstanceDefinition debug:
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_debug SET message = @message, delay_seconds = @delay WHERE action_instance_id = @i;",
                        ("i", instance.Id), ("message", (object)debug.Message ?? DBNull.Value),
                        ("delay", debug.DelaySeconds == 0f ? DBNull.Value : (object)(double)debug.DelaySeconds));
                    break;
                case StatIncreaseInstanceDefinition stat:
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_stat_increase SET stat_key = @key, amount = @amount WHERE action_instance_id = @i;",
                        ("i", instance.Id), ("key", stat.StatKey ?? ""), ("amount", (double)stat.Amount));
                    break;
                case IncrementProgressInstanceDefinition progress:
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_increment_progress SET amount = @amount WHERE action_instance_id = @i;",
                        ("i", instance.Id), ("amount", (double)progress.Amount));
                    break;
                case ModifyTemperatureInstanceDefinition temperature:
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_modify_temperature SET temperature_id = @temp, amount = @amount WHERE action_instance_id = @i;",
                        ("temp", temperature.TemperatureId ?? ""), ("amount", (double)temperature.Amount), ("i", instance.Id));
                    break;
                case CutsceneInstanceDefinition cutscene:
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_cutscene SET resource_id = @resource WHERE action_instance_id = @i;",
                        ("resource", cutscene.ResourceId ?? ""), ("i", instance.Id));
                    break;
                case PromptChoiceInstanceDefinition choice:
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_prompt_choice SET prompt = @prompt WHERE action_instance_id = @i;",
                        ("prompt", choice.Prompt ?? ""), ("i", instance.Id));
                    SyncChoiceOptions(connection, transaction, instance.Id, choice.Options);
                    break;
                case PhaseGotoInstanceDefinition phaseGoto:
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_phase_goto SET phase_exit_id = @exit WHERE action_instance_id = @i;",
                        ("exit", (object)phaseGoto.PhaseExitId ?? DBNull.Value), ("i", instance.Id));
                    break;
                case SessionGotoInstanceDefinition sessionGoto:
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_session_goto SET label = @label WHERE action_instance_id = @i;",
                        ("label", sessionGoto.Label ?? ""), ("i", instance.Id));
                    break;
                case WaitForContinueInstanceDefinition wait:
                case ReturnInstanceDefinition returnInstance:
                case EndSessionInstanceDefinition endSession:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"No persistence mapping for action instance '{instance.Id}' of type '{instance.GetType().Name}'.");
            }
        }

        private static void SyncChoiceOptions(DbConnection connection, DbTransaction transaction,
            string choiceInstanceId, IList<PromptChoiceOptionDefinition> desired)
        {
            desired = desired ?? new List<PromptChoiceOptionDefinition>();
            var existing = LoadOptions(connection, transaction, choiceInstanceId);
            var desiredIds = new HashSet<string>();
            foreach (var option in desired)
            {
                if (option == null || string.IsNullOrEmpty(option.Id))
                    throw new InvalidOperationException($"Choice instance '{choiceInstanceId}' contains an option with no id.");
                if (option.Sequence == null || string.IsNullOrEmpty(option.Sequence.Id))
                    throw new InvalidOperationException($"Choice option '{option.Id}' has a sequence with no id.");
                if (!desiredIds.Add(option.Id))
                    throw new InvalidOperationException($"Choice instance '{choiceInstanceId}' contains duplicate option id '{option.Id}'.");
            }

            foreach (var stored in existing.Values)
            {
                if (!desiredIds.Contains(stored.Id))
                {
                    DeleteOwnedSequence(connection, transaction, stored.SequenceId);
                }
            }

            ShiftOptionOrdinals(connection, transaction, choiceInstanceId);
            for (var ordinal = 0; ordinal < desired.Count; ordinal++)
            {
                var option = desired[ordinal];
                if (existing.TryGetValue(option.Id, out var stored))
                {
                    if (!string.Equals(stored.SequenceId, option.Sequence.Id, StringComparison.Ordinal))
                    {
                        EnsureSequence(connection, transaction, option.Sequence.Id);
                        Sync(connection, transaction, option.Sequence);
                        Sql.Execute(connection, transaction,
                            "UPDATE action_instance_choice_option SET ordinal = @ordinal, label = @label, action_sequence_id = @seq WHERE id = @id AND action_instance_id = @instance;",
                            ("ordinal", ordinal), ("label", option.Label ?? ""), ("seq", option.Sequence.Id),
                            ("id", option.Id), ("instance", choiceInstanceId));
                        DeleteOwnedSequence(connection, transaction, stored.SequenceId);
                    }
                    else
                    {
                        Sql.Execute(connection, transaction,
                            "UPDATE action_instance_choice_option SET ordinal = @ordinal, label = @label WHERE id = @id AND action_instance_id = @instance;",
                            ("ordinal", ordinal), ("label", option.Label ?? ""),
                            ("id", option.Id), ("instance", choiceInstanceId));
                        Sync(connection, transaction, option.Sequence);
                    }
                }
                else
                {
                    EnsureSequence(connection, transaction, option.Sequence.Id);
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_choice_option (id, action_instance_id, ordinal, label, action_sequence_id) " +
                        "VALUES (@id, @instance, @ordinal, @label, @seq);",
                        ("id", option.Id), ("instance", choiceInstanceId), ("ordinal", ordinal),
                        ("label", option.Label ?? ""), ("seq", option.Sequence.Id));
                    Sync(connection, transaction, option.Sequence);
                }
            }
        }

        private static void DeleteOwnedSequence(DbConnection connection, DbTransaction transaction, string sequenceId)
        {
            Sql.Execute(connection, transaction,
                "DELETE FROM action_sequence WHERE id = @id;", ("id", sequenceId));
        }

        private static void WriteInstance(DbConnection connection, DbTransaction transaction, string sequenceId, ActionInstanceDefinition instance, int ordinal)
        {
            if (string.IsNullOrEmpty(instance.Id))
            {
                throw new InvalidOperationException($"Instance of type '{instance.GetType().Name}' in sequence '{sequenceId}' has no id.");
            }

            var type = InstanceTypeName(instance);
            if (!instance.IsBlocking && ActionType.IsAlwaysBlocking(type))
            {
                throw new InvalidOperationException(
                    $"Flow-control instance '{instance.Id}' ({type}) cannot be nonblocking.");
            }

            Sql.Execute(connection, transaction,
                "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                "VALUES (@id, @seq, @ordinal, @type, @blocking);",
                ("id", instance.Id), ("seq", sequenceId), ("ordinal", ordinal), ("type", type),
                ("blocking", instance.IsBlocking ? 1 : 0));

            switch (instance)
            {
                case DebugInstanceDefinition debug:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_debug (action_instance_id, message, delay_seconds) VALUES (@i, @message, @delay);",
                        ("i", instance.Id),
                        ("message", (object)debug.Message ?? DBNull.Value),
                        ("delay", debug.DelaySeconds == 0f ? DBNull.Value : (object)(double)debug.DelaySeconds));
                    break;

                case StatIncreaseInstanceDefinition stat:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_stat_increase (action_instance_id, stat_key, amount) VALUES (@i, @key, @amount);",
                        ("i", instance.Id), ("key", stat.StatKey), ("amount", (double)stat.Amount));
                    break;

                case IncrementProgressInstanceDefinition progress:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_increment_progress (action_instance_id, amount) VALUES (@i, @amount);",
                        ("i", instance.Id), ("amount", (double)progress.Amount));
                    break;

                case ModifyTemperatureInstanceDefinition temperature:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_modify_temperature (action_instance_id, temperature_id, amount) VALUES (@i, @temp, @amount);",
                        ("i", instance.Id), ("temp", temperature.TemperatureId), ("amount", (double)temperature.Amount));
                    break;

                case CutsceneInstanceDefinition cutscene:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_cutscene (action_instance_id, resource_id) VALUES (@i, @resource);",
                        ("i", instance.Id), ("resource", cutscene.ResourceId));
                    break;

                case PromptChoiceInstanceDefinition choice:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_prompt_choice (action_instance_id, prompt) VALUES (@i, @prompt);",
                        ("i", instance.Id), ("prompt", choice.Prompt));

                    for (var optionOrdinal = 0; optionOrdinal < choice.Options.Count; optionOrdinal++)
                    {
                        var option = choice.Options[optionOrdinal];
                        if (string.IsNullOrEmpty(option.Id)) throw new InvalidOperationException($"Choice option in instance '{instance.Id}' has no id.");

                        // Nested sequences are their own rows, referenced by the option.
                        WriteOptionSequence(connection, transaction, instance.Id, option, optionOrdinal);
                    }
                    break;

                case WaitForContinueInstanceDefinition wait:
                    // Code-defined no-parameter action; its action_instance row
                    // is the complete persisted representation.
                    break;

                case PhaseGotoInstanceDefinition phaseGoto:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_phase_goto (action_instance_id, phase_exit_id) VALUES (@i, @exit);",
                        ("i", instance.Id),
                        ("exit", string.IsNullOrEmpty(phaseGoto.PhaseExitId) ? DBNull.Value : (object)phaseGoto.PhaseExitId));
                    break;

                case SessionGotoInstanceDefinition sessionGoto:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_session_goto (action_instance_id, label) VALUES (@i, @label);",
                        ("i", instance.Id), ("label", sessionGoto.Label));
                    break;

                case ReturnInstanceDefinition returnInstance:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_return (action_instance_id) VALUES (@i);",
                        ("i", instance.Id));
                    break;

                case EndSessionInstanceDefinition endSession:
                    Sql.Execute(connection, transaction,
                        "INSERT INTO action_instance_end_session (action_instance_id) VALUES (@i);",
                        ("i", instance.Id));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"No persistence mapping for action instance '{instance.Id}' of type '{instance.GetType().Name}'.");
            }
        }

        private static void WriteOptionSequence(
            DbConnection connection, DbTransaction transaction,
            string choiceInstanceId, PromptChoiceOptionDefinition option, int optionOrdinal)
        {
            if (string.IsNullOrEmpty(option.Sequence.Id))
            {
                throw new InvalidOperationException($"Option '{option.Id}' of choice instance '{choiceInstanceId}' has a sequence with no id.");
            }

            Sql.Execute(connection, transaction,
                "INSERT INTO action_sequence (id) VALUES (@id) ON CONFLICT(id) DO NOTHING;",
                ("id", option.Sequence.Id));

            for (var ordinal = 0; ordinal < option.Sequence.Instances.Count; ordinal++)
            {
                WriteInstance(connection, transaction, option.Sequence.Id, option.Sequence.Instances[ordinal], ordinal);
            }

            Sql.Execute(connection, transaction,
                "INSERT INTO action_instance_choice_option (id, action_instance_id, ordinal, label, action_sequence_id) " +
                "VALUES (@id, @instance, @ordinal, @label, @seq);",
                ("id", option.Id), ("instance", choiceInstanceId), ("ordinal", optionOrdinal),
                ("label", option.Label), ("seq", option.Sequence.Id));
        }

        private static string InstanceTypeName(ActionInstanceDefinition instance)
        {
            if (instance is DebugInstanceDefinition) return ActionType.Debug;
            if (instance is StatIncreaseInstanceDefinition) return ActionType.StatIncrease;
            if (instance is IncrementProgressInstanceDefinition) return ActionType.IncrementProgressV2;
            if (instance is ModifyTemperatureInstanceDefinition) return ActionType.ModifyTemperatureV2;
            if (instance is CutsceneInstanceDefinition) return ActionType.Cutscene;
            if (instance is PromptChoiceInstanceDefinition) return ActionType.PromptChoiceV2;
            if (instance is WaitForContinueInstanceDefinition) return ActionType.WaitForContinueV2;
            if (instance is PhaseGotoInstanceDefinition) return ActionType.PhaseGotoV2;
            if (instance is SessionGotoInstanceDefinition) return ActionType.SessionGotoV2;
            if (instance is ReturnInstanceDefinition) return ActionType.ReturnV2;
            if (instance is EndSessionInstanceDefinition) return ActionType.EndSessionV2;

            throw new InvalidOperationException($"Unknown action instance type '{instance.GetType().Name}'.");
        }
    }
}
