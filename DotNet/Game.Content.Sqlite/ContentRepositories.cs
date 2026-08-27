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
    /// the minimum the Workbench needs to author a card whose default Progress
    /// Action is an ordinary editable instance.
    ///
    /// Per the Ticket 05 contract, Create owns sequence construction: a card
    /// without a sequence gets a fresh owned one seeded with the default
    /// IncrementProgress(+10) instance. An author-supplied sequence is used
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
                        "INSERT INTO card (id, title, action_sequence_id) VALUES (@id, @title, @seq);",
                        ("id", card.Id), ("title", (object)card.Title ?? DBNull.Value), ("seq", card.Sequence.Id));
                    ReplaceTags(connection, transaction, card.Id, card.Tags);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void ReplaceTags(DbConnection connection, DbTransaction transaction, string cardId, IReadOnlyList<string> tags)
        {
            Sql.Execute(connection, transaction,
                "DELETE FROM card_tag WHERE card_id = @card;", ("card", cardId));
            Sql.EnsureTags(connection, transaction, tags);
            for (var i = 0; i < tags.Count; i++)
            {
                Sql.Execute(connection, transaction,
                    "INSERT INTO card_tag (card_id, tag_id, ordinal) VALUES (@card, @tag, @ordinal);",
                    ("card", cardId), ("tag", tags[i]), ("ordinal", i));
            }
        }
    }
}
