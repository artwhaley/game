using System;
using System.Data.Common;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Explicit authoring operations for code-defined Action Instances. The
    /// registry validates owner scope; subtype updates are intentionally
    /// spelled out per Action Type (no reflection and no EAV storage).
    /// </summary>
    public static class ActionInstanceRepository
    {
        public static void AppendDefault(DbConnection connection, string sequenceId,
            ActionOwnerScope scope, string typeKey, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) throw new ArgumentException("Instance id required.", nameof(instanceId));
            var info = ActionTypeRegistry.ByTypeKey(typeKey);
            var instance = info.DefaultInstance();
            instance.Id = instanceId;
            Append(connection, sequenceId, scope, instance);
        }

        /// <summary>Appends a fully configured instance (used by FK-backed pickers and duplicate).</summary>
        public static void Append(DbConnection connection, string sequenceId,
            ActionOwnerScope scope, ActionInstanceDefinition instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (string.IsNullOrEmpty(instance.Id)) throw new ArgumentException("Instance id required.", nameof(instance));
            ActionTypeRegistry.ValidateScope(instance, scope);

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var ordinal = 0;
                    Sql.QueryAll(connection,
                        "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM action_instance WHERE action_sequence_id = @seq;",
                        reader => ordinal = reader.GetInt32(0), ("seq", sequenceId));
                    ActionSequenceWriter.WriteSingleAt(connection, transaction, sequenceId, instance, ordinal);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Inserts a configured instance at an ordinal, shifting later rows safely.</summary>
        public static void Insert(DbConnection connection, string sequenceId,
            ActionOwnerScope scope, ActionInstanceDefinition instance, int ordinal)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            ActionTypeRegistry.ValidateScope(instance, scope);
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance SET ordinal = ordinal + 1000000 " +
                        "WHERE action_sequence_id = @seq AND ordinal >= @ordinal;",
                        ("seq", sequenceId), ("ordinal", ordinal));
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance SET ordinal = ordinal - 999999 " +
                        "WHERE action_sequence_id = @seq AND ordinal >= 1000000;",
                        ("seq", sequenceId));
                    ActionSequenceWriter.WriteSingleAt(connection, transaction, sequenceId, instance, ordinal);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void Delete(DbConnection connection, string instanceId)
        {
            Sql.Execute(connection, null, "DELETE FROM action_instance WHERE id = @id;", ("id", instanceId));
        }

        public static void Update(DbConnection connection, string instanceId, string typeKey,
            string textValue, float numberValue)
        {
            switch (typeKey)
            {
                case ActionTypeKeys.Debug:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_debug SET message = @text, delay_seconds = @number WHERE action_instance_id = @id;",
                        ("text", (object)textValue ?? DBNull.Value), ("number", (double)numberValue), ("id", instanceId));
                    break;
                case ActionTypeKeys.StatIncrease:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_stat_increase SET stat_key = @text, amount = @number WHERE action_instance_id = @id;",
                        ("text", textValue ?? ""), ("number", (double)numberValue), ("id", instanceId));
                    break;
                case ActionTypeKeys.IncrementProgress:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_increment_progress SET amount = @number WHERE action_instance_id = @id;",
                        ("number", (double)numberValue), ("id", instanceId));
                    break;
                case ActionTypeKeys.ModifyTemperature:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_modify_temperature SET temperature_id = @text, amount = @number WHERE action_instance_id = @id;",
                        ("text", textValue ?? ""), ("number", (double)numberValue), ("id", instanceId));
                    break;
                case ActionTypeKeys.Cutscene:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_cutscene SET resource_id = @text WHERE action_instance_id = @id;",
                        ("text", textValue ?? ""), ("id", instanceId));
                    break;
                case ActionTypeKeys.PromptChoice:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_prompt_choice SET prompt = @text WHERE action_instance_id = @id;",
                        ("text", textValue ?? ""), ("id", instanceId));
                    break;
                case ActionTypeKeys.PhaseGoto:
                    PhaseGraphRepository.SetPhaseGotoExit(connection, instanceId, textValue);
                    break;
                case ActionTypeKeys.SessionGoto:
                    SessionDecisionRepository.SetSessionGotoLabel(connection, instanceId, textValue ?? "");
                    break;
                case ActionTypeKeys.WaitForContinue:
                case ActionTypeKeys.Return:
                case ActionTypeKeys.EndSession:
                    break;
                default:
                    throw new InvalidOperationException("ActionTypeRegistry: unsupported update key '" + typeKey + "'.");
            }
        }

        public static void Move(DbConnection connection, string sequenceId, string instanceId, string otherInstanceId)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var first = -1;
                    var second = -1;
                    Sql.QueryAll(connection,
                        "SELECT id, ordinal FROM action_instance WHERE action_sequence_id = @seq AND id IN (@first, @second);",
                        reader =>
                        {
                            var id = reader.GetString(0);
                            if (id == instanceId) first = reader.GetInt32(1); else second = reader.GetInt32(1);
                        },
                        ("seq", sequenceId), ("first", instanceId), ("second", otherInstanceId));
                    if (first < 0 || second < 0) throw new InvalidOperationException("Action instance reorder target not found.");
                    // Move through a valid, collision-free ordinal; the schema
                    // deliberately rejects negative ordinals.
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance SET ordinal = 1000000 WHERE id = @id;", ("id", instanceId));
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance SET ordinal = @ordinal WHERE id = @id;",
                        ("ordinal", first), ("id", otherInstanceId));
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance SET ordinal = @ordinal WHERE id = @id;",
                        ("ordinal", second), ("id", instanceId));
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
