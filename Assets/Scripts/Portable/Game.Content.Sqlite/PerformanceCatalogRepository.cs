using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 03: repositories for the WPF-owned semantic Performance Tag
    /// catalog and the reusable Conversation Performance Events. Stable IDs are
    /// identity; titles may repeat. A tag referenced by an event or (through the
    /// catalog) by Unity ingredients is retired rather than hard-deleted, so
    /// existing content keeps resolving.
    /// </summary>
    public static class PerformanceCatalogRepository
    {
        // ---- Performance Tags ----

        public static void CreateTag(DbConnection connection, PerformanceTagDefinition tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (string.IsNullOrEmpty(tag.Id)) throw new ArgumentException("Performance tag id required.", nameof(tag.Id));
            Sql.Execute(connection, null,
                "INSERT OR IGNORE INTO performance_tag_definition (id, title, sort_order, is_retired) " +
                "VALUES (@id, @title, @sort, @retired);",
                ("id", tag.Id), ("title", (object)tag.Title ?? ""), ("sort", tag.SortOrder),
                ("retired", tag.IsRetired ? 1 : 0));
        }

        public static void UpdateTag(DbConnection connection, PerformanceTagDefinition tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (string.IsNullOrEmpty(tag.Id)) throw new ArgumentException("Performance tag id required.", nameof(tag.Id));
            Sql.Execute(connection, null,
                "UPDATE performance_tag_definition SET title = @title, sort_order = @sort, is_retired = @retired WHERE id = @id;",
                ("title", (object)tag.Title ?? ""), ("sort", tag.SortOrder),
                ("retired", tag.IsRetired ? 1 : 0), ("id", tag.Id));
        }

        public static void RenameTag(DbConnection connection, string id, string title)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Performance tag id required.", nameof(id));
            Sql.Execute(connection, null,
                "UPDATE performance_tag_definition SET title = @title WHERE id = @id;",
                ("title", (object)title ?? ""), ("id", id));
        }

        /// <summary>Retire keeps identity and resolution while hiding the tag from new selections.</summary>
        public static void SetRetired(DbConnection connection, string id, bool retired)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Performance tag id required.", nameof(id));
            Sql.Execute(connection, null,
                "UPDATE performance_tag_definition SET is_retired = @retired WHERE id = @id;",
                ("retired", retired ? 1 : 0), ("id", id));
        }

        public static List<PerformanceTagDefinition> ListTags(DbConnection connection)
        {
            var result = new List<PerformanceTagDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, title, sort_order, is_retired FROM performance_tag_definition ORDER BY sort_order, id;",
                reader => result.Add(new PerformanceTagDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    SortOrder = reader.GetInt32(2),
                    IsRetired = reader.GetInt64(3) == 1,
                }));
            return result;
        }

        /// <summary>One tag by stable id, or null. Used to snapshot undo state.</summary>
        public static PerformanceTagDefinition ReadTag(DbConnection connection, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            PerformanceTagDefinition tag = null;
            Sql.QueryAll(connection,
                "SELECT id, title, sort_order, is_retired FROM performance_tag_definition WHERE id = @id;",
                reader => tag = new PerformanceTagDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    SortOrder = reader.GetInt32(2),
                    IsRetired = reader.GetInt64(3) == 1,
                },
                ("id", id));
            return tag;
        }

        /// <summary>One event with all of its relations by stable id, or null.</summary>
        public static ConversationPerformanceEventDefinition ReadEvent(DbConnection connection, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return ListEvents(connection).Find(item => item.Id == id);
        }

        /// <summary>Event references for one Performance Tag (DB-side usage; Unity ingredient membership is in the catalog).</summary>
        public static int GetTagUsage(DbConnection connection, string tagId)
        {
            var count = 0;
            Sql.QueryAll(connection,
                "SELECT COUNT(*) FROM performance_event_tag WHERE performance_tag_id = @id;",
                reader => count = reader.GetInt32(0),
                ("id", tagId));
            return count;
        }

        /// <summary>Hard delete only when nothing references the tag; otherwise the caller retires it.</summary>
        public static void DeleteTagIfUnused(DbConnection connection, string tagId)
        {
            if (string.IsNullOrEmpty(tagId)) throw new ArgumentException("Performance tag id required.", nameof(tagId));
            var usage = GetTagUsage(connection, tagId);
            if (usage > 0)
            {
                throw new InvalidOperationException(
                    $"Performance tag '{tagId}' is referenced by {usage} event(s); retire it instead of deleting.");
            }
            Sql.Execute(connection, null, "DELETE FROM performance_tag_definition WHERE id = @id;", ("id", tagId));
        }

        // ---- Conversation Performance Events ----

        public static void SaveEvent(DbConnection connection, ConversationPerformanceEventDefinition performanceEvent)
        {
            if (performanceEvent == null) throw new ArgumentNullException(nameof(performanceEvent));
            if (string.IsNullOrEmpty(performanceEvent.Id))
                throw new ArgumentException("Performance event id required.", nameof(performanceEvent.Id));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT INTO conversation_performance_event " +
                        "(id, name, require_all_tags, staging_policy, named_anchor_id, refresh_at_dialogue_start, sort_order) " +
                        "VALUES (@id, @name, @all, @staging, @named, @refresh, @sort) " +
                        "ON CONFLICT(id) DO UPDATE SET name = @name, require_all_tags = @all, " +
                        "staging_policy = @staging, named_anchor_id = @named, refresh_at_dialogue_start = @refresh, sort_order = @sort;",
                        ("id", performanceEvent.Id), ("name", performanceEvent.Name ?? ""),
                        ("all", performanceEvent.RequireAllTags ? 1 : 0), ("staging", (int)performanceEvent.StagingPolicy),
                        ("named", string.IsNullOrEmpty(performanceEvent.NamedAnchorId)
                            ? (object)DBNull.Value
                            : performanceEvent.NamedAnchorId),
                        ("refresh", performanceEvent.RefreshAtDialogueStart ? 1 : 0), ("sort", performanceEvent.SortOrder));

                    ReplaceEventTags(connection, transaction, performanceEvent.Id, performanceEvent.PerformanceTagIds);
                    ReplaceEventAllowedAnchors(connection, transaction, performanceEvent.Id, performanceEvent.AllowedAnchorIds);
                    ReplaceEventAllowedPostures(connection, transaction, performanceEvent.Id, performanceEvent.AllowedPostureIds);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static List<ConversationPerformanceEventDefinition> ListEvents(DbConnection connection)
        {
            var result = new List<ConversationPerformanceEventDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, name, require_all_tags, staging_policy, named_anchor_id, refresh_at_dialogue_start, sort_order " +
                "FROM conversation_performance_event ORDER BY sort_order, id;",
                reader => result.Add(new ConversationPerformanceEventDefinition
                {
                    Id = reader.GetString(0),
                    Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    RequireAllTags = reader.GetInt64(2) == 1,
                    StagingPolicy = (PerformanceStagingPolicy)reader.GetInt32(3),
                    NamedAnchorId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    RefreshAtDialogueStart = reader.GetInt64(5) == 1,
                    SortOrder = reader.GetInt32(6),
                }));

            foreach (var performanceEvent in result)
            {
                Sql.QueryAll(connection,
                    "SELECT performance_tag_id FROM performance_event_tag WHERE event_id = @id ORDER BY ordinal;",
                    reader => performanceEvent.PerformanceTagIds.Add(reader.GetString(0)), ("id", performanceEvent.Id));
                Sql.QueryAll(connection,
                    "SELECT anchor_id FROM performance_event_allowed_anchor WHERE event_id = @id ORDER BY ordinal;",
                    reader => performanceEvent.AllowedAnchorIds.Add(reader.GetString(0)), ("id", performanceEvent.Id));
                Sql.QueryAll(connection,
                    "SELECT posture_id FROM performance_event_allowed_posture WHERE event_id = @id ORDER BY ordinal;",
                    reader => performanceEvent.AllowedPostureIds.Add(reader.GetString(0)), ("id", performanceEvent.Id));
            }
            return result;
        }

        public static int GetEventUsage(DbConnection connection, string eventId)
        {
            var count = 0;
            Sql.QueryAll(connection,
                "SELECT COUNT(*) FROM action_instance_perform WHERE event_id = @id;",
                reader => count = reader.GetInt32(0), ("id", eventId));
            return count;
        }

        public static void DeleteEventIfUnused(DbConnection connection, string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) throw new ArgumentException("Performance event id required.", nameof(eventId));
            var usage = GetEventUsage(connection, eventId);
            if (usage > 0)
            {
                throw new InvalidOperationException(
                    $"Performance event '{eventId}' is referenced by {usage} Perform action(s); delete blocked.");
            }
            Sql.Execute(connection, null, "DELETE FROM conversation_performance_event WHERE id = @id;", ("id", eventId));
        }

        private static void ReplaceEventTags(DbConnection connection, DbTransaction transaction,
            string eventId, IReadOnlyList<string> tagIds)
        {
            tagIds = tagIds ?? Array.Empty<string>();
            Sql.Execute(connection, transaction,
                "DELETE FROM performance_event_tag WHERE event_id = @id;", ("id", eventId));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordinal = 0;
            foreach (var tagId in tagIds)
            {
                if (string.IsNullOrEmpty(tagId) || !seen.Add(tagId)) continue;
                Sql.Execute(connection, transaction,
                    "INSERT INTO performance_event_tag (event_id, performance_tag_id, ordinal) VALUES (@id, @tag, @ordinal);",
                    ("id", eventId), ("tag", tagId), ("ordinal", ordinal++));
            }
        }

        private static void ReplaceEventAllowedAnchors(DbConnection connection, DbTransaction transaction,
            string eventId, IReadOnlyList<string> anchorIds)
        {
            anchorIds = anchorIds ?? Array.Empty<string>();
            Sql.Execute(connection, transaction,
                "DELETE FROM performance_event_allowed_anchor WHERE event_id = @id;", ("id", eventId));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordinal = 0;
            foreach (var anchorId in anchorIds)
            {
                if (string.IsNullOrEmpty(anchorId) || !seen.Add(anchorId)) continue;
                Sql.Execute(connection, transaction,
                    "INSERT INTO performance_event_allowed_anchor (event_id, anchor_id, ordinal) VALUES (@id, @anchor, @ordinal);",
                    ("id", eventId), ("anchor", anchorId), ("ordinal", ordinal++));
            }
        }

        private static void ReplaceEventAllowedPostures(DbConnection connection, DbTransaction transaction,
            string eventId, IReadOnlyList<string> postureIds)
        {
            postureIds = postureIds ?? Array.Empty<string>();
            Sql.Execute(connection, transaction,
                "DELETE FROM performance_event_allowed_posture WHERE event_id = @id;", ("id", eventId));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordinal = 0;
            foreach (var postureId in postureIds)
            {
                if (string.IsNullOrEmpty(postureId) || !seen.Add(postureId)) continue;
                Sql.Execute(connection, transaction,
                    "INSERT INTO performance_event_allowed_posture (event_id, posture_id, ordinal) VALUES (@id, @posture, @ordinal);",
                    ("id", eventId), ("posture", postureId), ("ordinal", ordinal++));
            }
        }
    }
}
