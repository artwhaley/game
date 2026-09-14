using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
            string textValue, float numberValue, float secondaryNumberValue = 0f, string patternValue = null,
            bool? blocking = null)
        {
            if (blocking.HasValue)
            {
                Sql.Execute(connection, null,
                    "UPDATE action_instance SET is_blocking = @blocking WHERE id = @id;",
                    ("blocking", blocking.Value ? 1 : 0), ("id", instanceId));
            }
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
                case ActionTypeKeys.Dialog:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_dialog SET dialog_text = @text WHERE action_instance_id = @id;",
                        ("text", textValue ?? ""), ("id", instanceId));
                    break;
                case ActionTypeKeys.Delay:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_delay SET duration_seconds = @number WHERE action_instance_id = @id;",
                        ("number", (double)Math.Max(0f, numberValue)), ("id", instanceId));
                    break;
                case ActionTypeKeys.ToyActivity:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_toy_activity SET capability_id = @text, pattern_resource_id = @pattern, duration_seconds = @secondary WHERE action_instance_id = @id;",
                        ("text", textValue ?? ""), ("pattern", patternValue ?? ""),
                        ("secondary", (double)Math.Max(0f, secondaryNumberValue)), ("id", instanceId));
                    break;
                case ActionTypeKeys.ToySetPattern:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_toy_set_pattern SET capability_id = @text, pattern_resource_id = @pattern WHERE action_instance_id = @id;",
                        ("text", textValue ?? ""), ("pattern", patternValue ?? ""), ("id", instanceId));
                    break;
                case ActionTypeKeys.DialogFromTags:
                    ReplaceDialogFromTagRelations(connection, instanceId, patternValue);
                    break;
                case ActionTypeKeys.Perform:
                    Sql.Execute(connection, null,
                        "UPDATE action_instance_perform SET event_id = @text WHERE action_instance_id = @id;",
                        ("text", (object)textValue ?? ""), ("id", instanceId));
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

        /// <summary>Replaces the required-tag relations of a DialogFromTags instance (tag list, ';'-joined).</summary>
        private static void ReplaceDialogFromTagRelations(DbConnection connection, string instanceId, string tagList)
        {
            var tagIds = (tagList ?? "")
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "DELETE FROM action_instance_dialog_from_tag WHERE action_instance_id = @i;", ("i", instanceId));
                    for (var i = 0; i < tagIds.Count; i++)
                    {
                        Sql.Execute(connection, transaction,
                            "INSERT INTO action_instance_dialog_from_tag (action_instance_id, dialog_tag_id, ordinal) VALUES (@i, @tag, @ordinal);",
                            ("i", instanceId), ("tag", tagIds[i]), ("ordinal", i));
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

        public static void MoveTo(DbConnection connection, string sequenceId, string instanceId, int ordinal)
        {
            var ids = new List<string>();
            Sql.QueryAll(connection,
                "SELECT id FROM action_instance WHERE action_sequence_id = @seq ORDER BY ordinal;",
                reader => ids.Add(reader.GetString(0)), ("seq", sequenceId));
            var current = ids.IndexOf(instanceId);
            if (current < 0) throw new InvalidOperationException("Action instance reorder target not found.");
            ids.RemoveAt(current);
            ordinal = Math.Max(0, Math.Min(ordinal, ids.Count));
            ids.Insert(ordinal, instanceId);
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Reorder(connection, transaction, "action_instance", "action_sequence_id", sequenceId,
                        "id", ids, ids.Count);
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
