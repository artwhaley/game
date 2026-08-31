using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>CRUD for the WPF-only Action Block template ledger.</summary>
    public static class ActionBlockRepository
    {
        public static void EnsureTable(DbConnection connection)
        {
            Sql.Execute(connection, null,
                "CREATE TABLE IF NOT EXISTS wpf_action_block (" +
                "id TEXT PRIMARY KEY, name TEXT NOT NULL, format_version INTEGER NOT NULL, " +
                "template_json TEXT NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0);" +
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_wpf_action_block_name ON wpf_action_block(name COLLATE NOCASE);");
        }

        public static List<ActionBlockDefinition> List(DbConnection connection, string search = null)
        {
            EnsureTable(connection);
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
            return new ActionBlockDefinition
            {
                Id = reader.GetString(0), Name = reader.GetString(1), FormatVersion = reader.GetInt32(2),
                TemplateJson = reader.GetString(3), SortOrder = reader.GetInt32(4)
            };
        }

        public static ActionBlockDefinition Get(DbConnection connection, string id)
        {
            EnsureTable(connection);
            ActionBlockDefinition result = null;
            Sql.QueryAll(connection,
                "SELECT id, name, format_version, template_json, sort_order FROM wpf_action_block WHERE id = @id;",
                reader => result = new ActionBlockDefinition
                {
                    Id = reader.GetString(0), Name = reader.GetString(1), FormatVersion = reader.GetInt32(2),
                    TemplateJson = reader.GetString(3), SortOrder = reader.GetInt32(4)
                }, ("id", id));
            return result;
        }

        public static void Create(DbConnection connection, ActionBlockDefinition block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            if (string.IsNullOrWhiteSpace(block.Id) || string.IsNullOrWhiteSpace(block.Name)) throw new ArgumentException("Action Block id and name are required.");
            if (block.FormatVersion <= 0) block.FormatVersion = ActionBlockSerializer.CurrentFormatVersion;
            EnsureTable(connection);
            Sql.Execute(connection, null,
                "INSERT INTO wpf_action_block (id, name, format_version, template_json, sort_order) VALUES (@id, @name, @version, @json, @sort);",
                ("id", block.Id), ("name", block.Name.Trim()), ("version", block.FormatVersion),
                ("json", block.TemplateJson ?? ""), ("sort", block.SortOrder));
        }

        public static void Rename(DbConnection connection, string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Action Block name is required.", nameof(name));
            EnsureTable(connection);
            Sql.Execute(connection, null, "UPDATE wpf_action_block SET name = @name WHERE id = @id;", ("name", name.Trim()), ("id", id));
        }

        public static void Delete(DbConnection connection, string id)
        {
            EnsureTable(connection);
            Sql.Execute(connection, null, "DELETE FROM wpf_action_block WHERE id = @id;", ("id", id));
        }

        public static void Duplicate(DbConnection connection, string sourceId, string newId, string newName)
        {
            var source = Get(connection, sourceId) ?? throw new InvalidOperationException("Action Block not found: " + sourceId);
            Create(connection, new ActionBlockDefinition
            {
                Id = newId, Name = newName, FormatVersion = source.FormatVersion,
                TemplateJson = source.TemplateJson, SortOrder = source.SortOrder + 1
            });
        }
    }
}
