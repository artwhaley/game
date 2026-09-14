using System;
using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Migration 5's in-transaction data transformation (Docs/MilestoneB/
    /// 02-schema-audit.md). Runs after every DDL statement of
    /// SQLITE-SCHEMA-V5-MILESTONE-B.sql, inside the same transaction,
    /// written against DbConnection/DbTransaction only.
    ///
    /// Steps:
    /// 1. Copy distinct legacy tag names used by Cards into card_tag_definition
    ///    (new stable opaque IDs, title preserved), then rebuild card_tag
    ///    assignments against those definitions preserving card/ordinal.
    /// 2. Seed session_card_weighting rows for every existing Session (defaults).
    /// 3. Rename card_tag_v5 to card_tag after dropping the legacy card_tag.
    /// 4. Drop superseded structures: tag/session_tag/phase_required_tag/
    ///    phase_excluded_tag (tag system), card_deck/card_deck_card (deck),
    ///    and the v1 legacy action tables + phase slots.
    ///
    /// Every statement tolerates an empty database (fresh installs). Unknown
    /// host tables (unity_*, wpf_*) are never touched.
    /// </summary>
    internal static class Migration5Transform
    {
        public static void Transform(DbConnection connection, DbTransaction transaction)
        {
            MigrateCardTags(connection, transaction);
            SeedSessionWeighting(connection, transaction);
            SwapCardTagTable(connection, transaction);
            DropLegacyTables(connection, transaction);
        }

        /// <summary>
        /// Old card_tag rows point at the shared tag table by slug-style ids
        /// (tag.id == tag.name). Distinct names become card_tag_definition
        /// rows with fresh opaque ids; assignments are re-pointed by name.
        /// </summary>
        private static void MigrateCardTags(DbConnection connection, DbTransaction transaction)
        {
            // name -> new definition id
            var tagIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var assignments = new List<(string CardId, int Ordinal, string TagName)>();

            ReadAll(connection, transaction,
                "SELECT ct.card_id, ct.ordinal, t.name FROM card_tag ct JOIN tag t ON t.id = ct.tag_id ORDER BY ct.card_id ASC, ct.ordinal ASC;",
                reader =>
                {
                    assignments.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
                });

            foreach (var assignment in assignments)
            {
                if (tagIds.ContainsKey(assignment.TagName)) continue;
                var newId = StableIds.New();
                tagIds.Add(assignment.TagName, newId);
                Sql.Execute(connection, transaction,
                    "INSERT INTO card_tag_definition (id, title, sort_order) VALUES (@id, @title, 0);",
                    ("id", newId), ("title", assignment.TagName));
            }

            foreach (var assignment in assignments)
            {
                Sql.Execute(connection, transaction,
                    "INSERT INTO card_tag_v5 (card_id, tag_id, ordinal) VALUES (@card, @tag, @ordinal);",
                    ("card", assignment.CardId), ("tag", tagIds[assignment.TagName]), ("ordinal", assignment.Ordinal));
            }
        }

        /// <summary>Defaults for existing Sessions (weighting is new in v5).</summary>
        private static void SeedSessionWeighting(DbConnection connection, DbTransaction transaction)
        {
            Sql.Execute(connection, transaction,
                "INSERT OR IGNORE INTO session_card_weighting (session_id) SELECT id FROM session;");
        }

        private static void SwapCardTagTable(DbConnection connection, DbTransaction transaction)
        {
            Sql.Execute(connection, transaction, "DROP TABLE card_tag;");
            Sql.Execute(connection, transaction, "ALTER TABLE card_tag_v5 RENAME TO card_tag;");
        }

        private static void DropLegacyTables(DbConnection connection, DbTransaction transaction)
        {
            // Tag system (phases lose classification; sessions classified by type).
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS session_tag;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS phase_required_tag;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS phase_excluded_tag;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS tag;");

            // Deck concept superseded by Phase Card queries.
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS card_deck_card;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS card_deck;");

            // v1 legacy action structures (unread since v2) and slots.
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS card_action;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS action_cutscene;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS choice_option;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS action_choice;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS action_stat_increase;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS action_debug;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS action;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS phase_slot_candidate;");
            Sql.Execute(connection, transaction, "DROP TABLE IF EXISTS phase_slot;");
        }

        private static void ReadAll(DbConnection connection, DbTransaction transaction, string sql, Action<DbDataReader> visit)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) visit(reader);
                }
            }
        }
    }
}
