using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Narrow v2 repository: owned Action Sequences and their instances.
    /// Persistence delegates to ActionSequenceWriter (one shared implementation
    /// for initializer + repositories); delete cascades instances and subtype
    /// rows via the schema's physical FKs.
    /// </summary>
    public static class ActionSequenceRepository
    {
        public static void Save(DbConnection connection, ActionSequenceDefinition sequence)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ActionSequenceWriter.Write(connection, transaction, sequence);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void Delete(DbConnection connection, string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) throw new ArgumentException("Sequence id required.", nameof(sequenceId));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ActionSequenceWriter.Delete(connection, transaction, sequenceId);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static bool Exists(DbConnection connection, string sequenceId)
        {
            var found = false;
            Sql.QueryAll(connection, "SELECT 1 FROM action_sequence WHERE id = @id;",
                reader => found = true, ("id", sequenceId));
            return found;
        }
    }

    /// <summary>
    /// Narrow v2 repository: Card creation with an owned Action Sequence —
    /// the minimum the Workbench needs to author a card whose default pacing
    /// and Progress Actions are ordinary editable instances.
    ///
    /// Create owns sequence construction: a card without a sequence gets a
    /// fresh owned one seeded with WaitForContinue followed by
    /// IncrementProgress(+10). An author-supplied sequence is used
    /// verbatim; the default is only applied when the card arrives without one,
    /// so deleting or changing it affects only that card.
    /// </summary>
    public static class CardRepository
    {
        public const float DefaultProgressAmount = 10f;

        public static void Create(DbConnection connection, CardDefinition card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (string.IsNullOrEmpty(card.Id)) throw new ArgumentException("Card id required.", nameof(card));

            if (card.Sequence == null || string.IsNullOrEmpty(card.Sequence.Id))
            {
                card.Sequence = new ActionSequenceDefinition
                {
                    Id = $"cseq-{card.Id}",
                    Instances =
                    {
                        new WaitForContinueInstanceDefinition
                        {
                            Id = $"inst-{card.Id}-default-wait",
                        },
                        new IncrementProgressInstanceDefinition
                        {
                            Id = $"inst-{card.Id}-default-progress",
                            Amount = DefaultProgressAmount,
                        },
                    },
                };
            }

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ActionSequenceWriter.Write(connection, transaction, card.Sequence);
                    Sql.Execute(connection, transaction,
                        "INSERT INTO card (id, title, body_text, action_sequence_id) VALUES (@id, @title, @body, @seq);",
                        ("id", card.Id), ("title", (object)card.Title ?? DBNull.Value),
                        ("body", (object)card.BodyText ?? DBNull.Value), ("seq", card.Sequence.Id));
                    ReplaceRelations(connection, transaction, card.Id, card.CardTagIds);
                    ReplaceRelationTable(connection, transaction, "card_kink", "card_id", "kink_id", card.Id, card.KinkIds);
                    ReplaceRelationTable(connection, transaction, "card_required_equipment", "card_id", "equipment_id", card.Id, card.RequiredEquipmentIds);
                    ReplaceRelationTable(connection, transaction, "card_required_smart_toy_capability", "card_id", "capability_id", card.Id, card.RequiredCapabilityIds);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void ReplaceRelations(DbConnection connection, DbTransaction transaction, string cardId, IReadOnlyList<string> tagIds)
        {
            Sql.Execute(connection, transaction,
                "DELETE FROM card_tag WHERE card_id = @card;", ("card", cardId));
            for (var i = 0; i < tagIds.Count; i++)
            {
                Sql.Execute(connection, transaction,
                    "INSERT INTO card_tag (card_id, tag_id, ordinal) VALUES (@card, @tag, @ordinal);",
                    ("card", cardId), ("tag", tagIds[i]), ("ordinal", i));
            }
        }

        // ---- Milestone B card authoring (Tickets 05/13) ----

        public static void Rename(DbConnection connection, string cardId, string title)
        {
            Sql.Execute(connection, null,
                "UPDATE card SET title = @title WHERE id = @id;",
                ("title", (object)title ?? DBNull.Value), ("id", cardId));
        }

        public static void SetBody(DbConnection connection, string cardId, string bodyText)
        {
            Sql.Execute(connection, null,
                "UPDATE card SET body_text = @body WHERE id = @id;",
                ("body", (object)bodyText ?? DBNull.Value), ("id", cardId));
        }

        public static void ReplaceRelations(DbConnection connection, CardDefinition card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ReplaceRelationTable(connection, transaction, "card_tag", "card_id", "tag_id", card.Id, card.CardTagIds);
                    ReplaceRelationTable(connection, transaction, "card_kink", "card_id", "kink_id", card.Id, card.KinkIds);
                    ReplaceRelationTable(connection, transaction, "card_required_equipment", "card_id", "equipment_id", card.Id, card.RequiredEquipmentIds);
                    ReplaceRelationTable(connection, transaction, "card_required_smart_toy_capability", "card_id", "capability_id", card.Id, card.RequiredCapabilityIds);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void ReplaceRelationTable(DbConnection connection, DbTransaction transaction,
            string table, string parentColumn, string relationColumn, string parentId, IReadOnlyList<string> relationIds)
        {
            Sql.Execute(connection, transaction,
                $"DELETE FROM {table} WHERE {parentColumn} = @parent;", ("parent", parentId));
            for (var i = 0; i < relationIds.Count; i++)
            {
                Sql.Execute(connection, transaction,
                    $"INSERT INTO {table} ({parentColumn}, {relationColumn}, ordinal) VALUES (@parent, @rel, @ordinal);",
                    ("parent", parentId), ("rel", relationIds[i]), ("ordinal", i));
            }
        }

        /// <summary>Deletes only card-owned rows: relations cascade physically; the sequence is removed explicitly.</summary>
        public static void Delete(DbConnection connection, string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) throw new ArgumentException("Card id required.", nameof(cardId));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    string sequenceId = null;
                    Sql.QueryAll(connection,
                        "SELECT action_sequence_id FROM card WHERE id = @id;",
                        reader => sequenceId = reader.IsDBNull(0) ? null : reader.GetString(0),
                        ("id", cardId));

                    Sql.Execute(connection, transaction, "DELETE FROM card WHERE id = @id;", ("id", cardId));
                    if (!string.IsNullOrEmpty(sequenceId))
                    {
                        ActionSequenceWriter.Delete(connection, transaction, sequenceId);
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

        /// <summary>
        /// Deep-clones a card to a new id: relations copied, Action Instances cloned to
        /// new ids, Resource references preserved. Returns the cloned definition.
        /// </summary>
        public static CardDefinition Duplicate(DbConnection connection, string sourceCardId, string newCardId, string title)
        {
            if (string.IsNullOrEmpty(sourceCardId)) throw new ArgumentException("Source card id required.", nameof(sourceCardId));
            if (string.IsNullOrEmpty(newCardId)) throw new ArgumentException("New card id required.", nameof(newCardId));

            string sourceTitle = null, sourceBody = null, sourceSequenceId = null;
            Sql.QueryAll(connection,
                "SELECT title, body_text, action_sequence_id FROM card WHERE id = @id;",
                reader =>
                {
                    sourceTitle = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    sourceBody = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    sourceSequenceId = reader.IsDBNull(2) ? null : reader.GetString(2);
                },
                ("id", sourceCardId));

            if (sourceSequenceId == null)
            {
                throw new InvalidOperationException($"Card '{sourceCardId}' has no owned sequence to duplicate.");
            }

            var clone = new CardDefinition
            {
                Id = newCardId,
                Title = string.IsNullOrEmpty(title) ? sourceTitle : title,
                BodyText = sourceBody,
            };

            clone.CardTagIds.AddRange(RelationIds(connection, "SELECT tag_id FROM card_tag WHERE card_id = @id ORDER BY ordinal;", sourceCardId));
            clone.KinkIds.AddRange(RelationIds(connection, "SELECT kink_id FROM card_kink WHERE card_id = @id ORDER BY ordinal;", sourceCardId));
            clone.RequiredEquipmentIds.AddRange(RelationIds(connection, "SELECT equipment_id FROM card_required_equipment WHERE card_id = @id ORDER BY ordinal;", sourceCardId));
            clone.RequiredCapabilityIds.AddRange(RelationIds(connection, "SELECT capability_id FROM card_required_smart_toy_capability WHERE card_id = @id ORDER BY ordinal;", sourceCardId));

            // Clone the sequence with fresh instance ids, preserving subtype values.
            var newSequenceId = $"cseq-{newCardId}";
            clone.Sequence = CloneSequence(connection, sourceSequenceId, newSequenceId, newCardId);

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ActionSequenceWriter.Write(connection, transaction, clone.Sequence);
                    Sql.Execute(connection, transaction,
                        "INSERT INTO card (id, title, body_text, action_sequence_id) VALUES (@id, @title, @body, @seq);",
                        ("id", clone.Id), ("title", (object)clone.Title ?? DBNull.Value),
                        ("body", (object)clone.BodyText ?? DBNull.Value), ("seq", clone.Sequence.Id));
                    ReplaceRelationTable(connection, transaction, "card_tag", "card_id", "tag_id", clone.Id, clone.CardTagIds);
                    ReplaceRelationTable(connection, transaction, "card_kink", "card_id", "kink_id", clone.Id, clone.KinkIds);
                    ReplaceRelationTable(connection, transaction, "card_required_equipment", "card_id", "equipment_id", clone.Id, clone.RequiredEquipmentIds);
                    ReplaceRelationTable(connection, transaction, "card_required_smart_toy_capability", "card_id", "capability_id", clone.Id, clone.RequiredCapabilityIds);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            return clone;
        }

        private static List<string> RelationIds(DbConnection connection, string sql, string cardId)
        {
            var ids = new List<string>();
            Sql.QueryAll(connection, sql, reader => ids.Add(reader.GetString(0)), ("id", cardId));
            return ids;
        }

        private static ActionSequenceDefinition CloneSequence(DbConnection connection, string sourceSequenceId, string newSequenceId, string newCardId)
        {
            var source = GameContentSnapshotLoader.LoadSequence(connection, sourceSequenceId);
            var clone = new ActionSequenceDefinition { Id = newSequenceId };
            for (var i = 0; i < source.Instances.Count; i++)
            {
                var instance = source.Instances[i];
                instance.Id = $"inst-{newCardId}-{i + 1}";
                clone.Instances.Add(instance);
            }
            return clone;
        }
    }
}
