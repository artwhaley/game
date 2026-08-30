using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 10: repositories for the Dialog Tag and Dialog Snippet catalogs.
    /// Stable IDs are identity; display names may repeat. Deleting a tag used
    /// by a snippet or a DialogFromTags action is blocked with usage counts;
    /// deleting a snippet never deletes tags. Snippet duplicate mints a fresh
    /// stable ID and copies Text + tags.
    /// </summary>
    public static class DialogCatalogRepository
    {
        // ---- Dialog Tags ----

        public static void CreateTag(DbConnection connection, DialogTagDefinition tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (string.IsNullOrEmpty(tag.Id)) throw new ArgumentException("Dialog tag id required.", nameof(tag.Id));
            Sql.Execute(connection, null,
                "INSERT OR IGNORE INTO dialog_tag_definition (id, title, sort_order) VALUES (@id, @title, @sort);",
                ("id", tag.Id), ("title", (object)tag.Title ?? DBNull.Value), ("sort", tag.SortOrder));
        }

        public static void RenameTag(DbConnection connection, string id, string title)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Dialog tag id required.", nameof(id));
            Sql.Execute(connection, null,
                "UPDATE dialog_tag_definition SET title = @title WHERE id = @id;",
                ("title", (object)title ?? DBNull.Value), ("id", id));
        }

        public static List<DialogTagDefinition> ListTags(DbConnection connection)
        {
            var result = new List<DialogTagDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, title, sort_order FROM dialog_tag_definition ORDER BY sort_order, id;",
                reader => result.Add(new DialogTagDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    SortOrder = reader.GetInt32(2),
                }));
            return result;
        }

        /// <summary>Snippet + DialogFromTags action references for one tag.</summary>
        public static DialogTagUsage GetTagUsage(DbConnection connection, string tagId)
        {
            var usage = new DialogTagUsage { TagId = tagId };
            Sql.QueryAll(connection,
                "SELECT (SELECT COUNT(*) FROM dialog_snippet_tag WHERE dialog_tag_id = @id) + " +
                "(SELECT COUNT(*) FROM action_instance_dialog_from_tag WHERE dialog_tag_id = @id);",
                reader => usage.TotalReferences = reader.GetInt32(0),
                ("id", tagId));
            return usage;
        }

        public static void DeleteTagIfUnused(DbConnection connection, string tagId)
        {
            if (string.IsNullOrEmpty(tagId)) throw new ArgumentException("Dialog tag id required.", nameof(tagId));
            var usage = GetTagUsage(connection, tagId);
            if (usage.TotalReferences > 0)
            {
                throw new InvalidOperationException(
                    $"Dialog tag '{tagId}' is referenced by {usage.TotalReferences} snippet(s)/action(s); delete blocked.");
            }
            Sql.Execute(connection, null, "DELETE FROM dialog_tag_definition WHERE id = @id;", ("id", tagId));
        }

        // ---- Dialog Snippets ----

        public static void CreateSnippet(DbConnection connection, DialogSnippetDefinition snippet)
        {
            if (snippet == null) throw new ArgumentNullException(nameof(snippet));
            if (string.IsNullOrEmpty(snippet.Id)) throw new ArgumentException("Dialog snippet id required.", nameof(snippet.Id));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT OR IGNORE INTO dialog_snippet (id, name, text, sort_order) VALUES (@id, @name, @text, @sort);",
                        ("id", snippet.Id), ("name", snippet.Name ?? ""), ("text", snippet.Text ?? ""), ("sort", snippet.SortOrder));
                    ReplaceSnippetTags(connection, transaction, snippet.Id, snippet.DialogTagIds);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void UpdateSnippet(DbConnection connection, DialogSnippetDefinition snippet)
        {
            if (snippet == null) throw new ArgumentNullException(nameof(snippet));
            if (string.IsNullOrEmpty(snippet.Id)) throw new ArgumentException("Dialog snippet id required.", nameof(snippet.Id));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "UPDATE dialog_snippet SET name = @name, text = @text, sort_order = @sort WHERE id = @id;",
                        ("name", snippet.Name ?? ""), ("text", snippet.Text ?? ""),
                        ("sort", snippet.SortOrder), ("id", snippet.Id));
                    ReplaceSnippetTags(connection, transaction, snippet.Id, snippet.DialogTagIds);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void SetSnippetName(DbConnection connection, string id, string name)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Dialog snippet id required.", nameof(id));
            Sql.Execute(connection, null,
                "UPDATE dialog_snippet SET name = @name WHERE id = @id;",
                ("name", name ?? ""), ("id", id));
        }

        public static void SetSnippetText(DbConnection connection, string id, string text)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Dialog snippet id required.", nameof(id));
            Sql.Execute(connection, null,
                "UPDATE dialog_snippet SET text = @text WHERE id = @id;",
                ("text", text ?? ""), ("id", id));
        }

        public static void SetSnippetTags(DbConnection connection, string id, IReadOnlyList<string> tagIds)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Dialog snippet id required.", nameof(id));
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ReplaceSnippetTags(connection, transaction, id, tagIds);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static List<DialogSnippetDefinition> ListSnippets(DbConnection connection)
        {
            var result = new List<DialogSnippetDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, name, text, sort_order FROM dialog_snippet ORDER BY sort_order, id;",
                reader => result.Add(new DialogSnippetDefinition
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Text = reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                }));

            foreach (var snippet in result)
            {
                Sql.QueryAll(connection,
                    "SELECT dialog_tag_id FROM dialog_snippet_tag WHERE dialog_snippet_id = @id ORDER BY ordinal;",
                    reader => snippet.DialogTagIds.Add(reader.GetString(0)),
                    ("id", snippet.Id));
            }
            return result;
        }

        /// <summary>Duplicates a snippet: new stable id, copied Name/Text/SortOrder/Tags. Returns the clone.</summary>
        public static DialogSnippetDefinition DuplicateSnippet(DbConnection connection, string sourceId, string newId)
        {
            if (string.IsNullOrEmpty(sourceId)) throw new ArgumentException("Source snippet id required.", nameof(sourceId));
            if (string.IsNullOrEmpty(newId)) throw new ArgumentException("New snippet id required.", nameof(newId));

            var source = ListSnippets(connection).Find(s => s.Id == sourceId)
                ?? throw new InvalidOperationException($"Dialog snippet '{sourceId}' not found.");

            var clone = new DialogSnippetDefinition
            {
                Id = newId,
                Name = source.Name,
                Text = source.Text,
                SortOrder = source.SortOrder,
            };
            clone.DialogTagIds.AddRange(source.DialogTagIds);
            CreateSnippet(connection, clone);
            return clone;
        }

        /// <summary>Deletes a snippet. Tags survive (relation rows cascade only).</summary>
        public static void DeleteSnippet(DbConnection connection, string snippetId)
        {
            if (string.IsNullOrEmpty(snippetId)) throw new ArgumentException("Dialog snippet id required.", nameof(snippetId));
            Sql.Execute(connection, null, "DELETE FROM dialog_snippet WHERE id = @id;", ("id", snippetId));
        }

        private static void ReplaceSnippetTags(DbConnection connection, DbTransaction transaction,
            string snippetId, IReadOnlyList<string> tagIds)
        {
            Sql.Execute(connection, transaction,
                "DELETE FROM dialog_snippet_tag WHERE dialog_snippet_id = @id;", ("id", snippetId));
            for (var i = 0; i < tagIds.Count; i++)
            {
                Sql.Execute(connection, transaction,
                    "INSERT OR IGNORE INTO dialog_snippet_tag (dialog_snippet_id, dialog_tag_id, ordinal) VALUES (@id, @tag, @ordinal);",
                    ("id", snippetId), ("tag", tagIds[i]), ("ordinal", i));
            }
        }
    }

    /// <summary>Usage summary for one Dialog Tag (Ticket 10 delete safety).</summary>
    public sealed class DialogTagUsage
    {
        public string TagId { get; set; } = "";
        public int TotalReferences { get; set; }
    }
}
