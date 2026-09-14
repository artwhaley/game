using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>CRUD for the WPF-only Action Block template ledger.</summary>
    public static class ActionBlockRepository
    {
        public static List<ActionBlockDefinition> List(DbConnection connection, string search = null)
        {
            RequireSchema(connection);
            var result = new List<ActionBlockDefinition>();
            var filter = (search ?? "").Trim();
            var sql = "SELECT id, name, format_version, template_json, sort_order FROM wpf_action_block " +
                (filter.Length == 0 ? "" : "WHERE name LIKE @search COLLATE NOCASE ") +
                "ORDER BY sort_order, name COLLATE NOCASE;";
            if (filter.Length == 0)
                Sql.QueryAll(connection, sql, reader => result.Add(Read(reader)));
            else
                Sql.QueryAll(connection, sql, reader => result.Add(Read(reader)), ("search", "%" + filter + "%"));
            return result;
        }

        private static ActionBlockDefinition Read(DbDataReader reader)
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            var formatVersion = reader.GetInt32(2);
            var templateJson = reader.GetString(3);
            var template = ActionBlockSerializer.Deserialize(templateJson);
            if (formatVersion != template.FormatVersion)
                throw new InvalidOperationException(
                    "Action Block '" + id + "' row format version " + formatVersion +
                    " does not match JSON format version " + template.FormatVersion + ".");
            return new ActionBlockDefinition
            {
                Id = id, Name = name, FormatVersion = formatVersion,
                TemplateJson = templateJson, SortOrder = reader.GetInt32(4),
                FolderPath = template.FolderPath
            };
        }

        public static ActionBlockDefinition Get(DbConnection connection, string id)
        {
            RequireSchema(connection);
            ActionBlockDefinition result = null;
            Sql.QueryAll(connection,
                "SELECT id, name, format_version, template_json, sort_order FROM wpf_action_block WHERE id = @id;",
                reader => result = Read(reader), ("id", id));
            return result;
        }

        public static void Create(DbConnection connection, ActionBlockDefinition block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            if (string.IsNullOrWhiteSpace(block.Id) || string.IsNullOrWhiteSpace(block.Name)) throw new ArgumentException("Action Block id and name are required.");
            ValidateDefinition(block);
            RequireSchema(connection);
            var templateJson = ActionBlockSerializer.SetFolderPath(block.TemplateJson, block.FolderPath);
            Sql.Execute(connection, null,
                "INSERT INTO wpf_action_block (id, name, format_version, template_json, sort_order) VALUES (@id, @name, @version, @json, @sort);",
                ("id", block.Id), ("name", block.Name.Trim()), ("version", block.FormatVersion),
                ("json", templateJson), ("sort", block.SortOrder));
        }

        public static void Rename(DbConnection connection, string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Action Block name is required.", nameof(name));
            RequireSchema(connection);
            Sql.Execute(connection, null, "UPDATE wpf_action_block SET name = @name WHERE id = @id;", ("name", name.Trim()), ("id", id));
        }

        public static void Delete(DbConnection connection, string id)
        {
            RequireSchema(connection);
            Sql.Execute(connection, null, "DELETE FROM wpf_action_block WHERE id = @id;", ("id", id));
        }

        public static void Duplicate(DbConnection connection, string sourceId, string newId, string newName)
        {
            var source = Get(connection, sourceId) ?? throw new InvalidOperationException("Action Block not found: " + sourceId);
            Create(connection, new ActionBlockDefinition
            {
                Id = newId, Name = newName, FormatVersion = source.FormatVersion,
                TemplateJson = source.TemplateJson, SortOrder = source.SortOrder + 1,
                FolderPath = source.FolderPath
            });
        }

        public static void SetFolderPath(DbConnection connection, string id, string folderPath)
        {
            var block = Get(connection, id) ?? throw new InvalidOperationException("Action Block not found: " + id);
            var json = ActionBlockSerializer.SetFolderPath(block.TemplateJson, folderPath);
            Sql.Execute(connection, null,
                "UPDATE wpf_action_block SET template_json = @json WHERE id = @id;",
                ("json", json), ("id", id));
        }

        private static void ValidateDefinition(ActionBlockDefinition block)
        {
            if (block.FormatVersion != ActionBlockSerializer.CurrentFormatVersion)
                throw new InvalidOperationException(
                    "Unsupported Action Block row format version " + block.FormatVersion +
                    "; expected " + ActionBlockSerializer.CurrentFormatVersion + ".");
            if (string.IsNullOrWhiteSpace(block.TemplateJson))
                throw new InvalidOperationException("Action Block template is empty.");
            var template = ActionBlockSerializer.Deserialize(block.TemplateJson);
            if (template.FormatVersion != block.FormatVersion)
                throw new InvalidOperationException(
                    "Action Block row format version " + block.FormatVersion +
                    " does not match JSON format version " + template.FormatVersion + ".");
        }

        private static void RequireSchema(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            var exists = false;
            Sql.QueryAll(connection,
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'wpf_action_block' LIMIT 1;",
                _ => exists = true);
            if (!exists)
            {
                throw new InvalidOperationException(
                    "Action Block schema is unavailable; run CoreMigrator.EnsureSchema before using ActionBlockRepository.");
            }
        }
    }
}
