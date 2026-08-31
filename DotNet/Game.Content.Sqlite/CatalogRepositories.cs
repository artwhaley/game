using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// v5 repositories for the authored catalog definitions (CardTag, Kink,
    /// Equipment, SmartToyCapability) and the completed SessionType. Stable
    /// IDs, narrow domain operations, usage queries for safe deletion —
    /// same posture as the v2 reference repositories.
    /// </summary>
    public static class CatalogRepositories
    {
        // ---- SessionType (v5 completion: sort_order + required capabilities) ----

        public static void CreateSessionType(DbConnection connection, SessionTypeDefinition type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrEmpty(type.Id)) throw new ArgumentException("SessionType id required.", nameof(type));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT OR IGNORE INTO session_type (id, title, sort_order) VALUES (@id, @title, @sort);",
                        ("id", type.Id), ("title", (object)type.Title ?? DBNull.Value), ("sort", type.SortOrder));
                    ReplaceSessionTypeCapabilities(connection, transaction, type);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void RenameSessionType(DbConnection connection, string id, string title)
        {
            Sql.Execute(connection, null,
                "UPDATE session_type SET title = @title WHERE id = @id;",
                ("title", (object)title ?? DBNull.Value), ("id", id));
        }

        public static void SetSessionTypeSortOrder(DbConnection connection, string id, int sortOrder)
        {
            Sql.Execute(connection, null,
                "UPDATE session_type SET sort_order = @sort WHERE id = @id;",
                ("sort", sortOrder), ("id", id));
        }

        public static void ReplaceSessionTypeCapabilities(DbConnection connection, DbTransaction transaction, SessionTypeDefinition type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            Sql.Execute(connection, transaction,
                "DELETE FROM session_type_required_smart_toy_capability WHERE session_type_id = @id;",
                ("id", type.Id));
            for (var i = 0; i < type.RequiredCapabilityIds.Count; i++)
            {
                Sql.Execute(connection, transaction,
                    "INSERT INTO session_type_required_smart_toy_capability (session_type_id, capability_id, ordinal) " +
                    "VALUES (@type, @cap, @ordinal);",
                    ("type", type.Id), ("cap", type.RequiredCapabilityIds[i]), ("ordinal", i));
            }
        }

        /// <summary>Updates title/sort/capabilities atomically.</summary>
        public static void UpdateSessionType(DbConnection connection, SessionTypeDefinition type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrEmpty(type.Id)) throw new ArgumentException("SessionType id required.", nameof(type));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "UPDATE session_type SET title = @title, sort_order = @sort WHERE id = @id;",
                        ("title", (object)type.Title ?? DBNull.Value), ("sort", type.SortOrder), ("id", type.Id));
                    ReplaceSessionTypeCapabilities(connection, transaction, type);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static List<SessionTypeDefinition> ListSessionTypes(DbConnection connection)
        {
            var result = new List<SessionTypeDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, title, sort_order FROM session_type ORDER BY sort_order, id;",
                reader =>
                {
                    var type = new SessionTypeDefinition
                    {
                        Id = reader.GetString(0),
                        Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        SortOrder = reader.GetInt32(2),
                    };
                    result.Add(type);
                });

            foreach (var type in result)
            {
                Sql.QueryAll(connection,
                    "SELECT capability_id FROM session_type_required_smart_toy_capability " +
                    "WHERE session_type_id = @id ORDER BY ordinal;",
                    reader => type.RequiredCapabilityIds.Add(reader.GetString(0)),
                    ("id", type.Id));
            }
            return result;
        }

        public static int CountSessionsOfType(DbConnection connection, string sessionTypeId)
        {
            var count = 0;
            Sql.QueryAll(connection,
                "SELECT COUNT(*) FROM session WHERE session_type_id = @id;",
                reader => count = reader.GetInt32(0),
                ("id", sessionTypeId));
            return count;
        }

        // ---- Card Tag definitions ----

        public static void CreateCardTag(DbConnection connection, CardTagDefinition tag)
        {
            InsertDefinition(connection, "card_tag_definition", tag?.Id, tag?.Title, tag.SortOrder,
                "Card tag", description: null, category: null);
        }

        public static void RenameCardTag(DbConnection connection, string id, string title)
        {
            Sql.Execute(connection, null,
                "UPDATE card_tag_definition SET title = @title WHERE id = @id;",
                ("title", (object)title ?? DBNull.Value), ("id", id));
        }

        public static void SetCardTagSortOrder(DbConnection connection, string id, int sortOrder)
        {
            SetDefinitionSortOrder(connection, "card_tag_definition", id, sortOrder);
        }

        public static List<CardTagDefinition> ListCardTags(DbConnection connection)
        {
            var result = new List<CardTagDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, title, sort_order FROM card_tag_definition ORDER BY sort_order, title;",
                reader => result.Add(new CardTagDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    SortOrder = reader.GetInt32(2),
                }));
            return result;
        }

        public static int CountCardTagUsage(DbConnection connection, string tagId)
        {
            var count = 0;
            Sql.QueryAll(connection,
                "SELECT (SELECT COUNT(*) FROM card_tag WHERE tag_id = @id) + " +
                "(SELECT COUNT(*) FROM phase_card_all_tag WHERE tag_id = @id) + " +
                "(SELECT COUNT(*) FROM phase_card_any_tag WHERE tag_id = @id);",
                reader => count = reader.GetInt32(0),
                ("id", tagId));
            return count;
        }

        // ---- Kink definitions ----

        public static void CreateKink(DbConnection connection, KinkDefinition kink)
        {
            if (kink == null) throw new ArgumentNullException(nameof(kink));
            InsertDefinition(connection, "kink_definition", kink.Id, kink.Title, kink.SortOrder,
                "Kink", kink.Description, category: null);
        }

        public static void UpdateKink(DbConnection connection, KinkDefinition kink)
        {
            if (kink == null) throw new ArgumentNullException(nameof(kink));
            Sql.Execute(connection, null,
                "UPDATE kink_definition SET title = @title, description = @desc, sort_order = @sort WHERE id = @id;",
                ("title", (object)kink.Title ?? DBNull.Value), ("desc", (object)kink.Description ?? DBNull.Value),
                ("sort", kink.SortOrder), ("id", kink.Id));
        }

        public static List<KinkDefinition> ListKinks(DbConnection connection)
        {
            var result = new List<KinkDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, title, description, sort_order FROM kink_definition ORDER BY sort_order, title;",
                reader => result.Add(new KinkDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                }));
            return result;
        }

        public static int CountKinkUsage(DbConnection connection, string kinkId)
        {
            return CountRelationUsage(connection, "card_kink", "kink_id", kinkId);
        }

        // ---- Equipment definitions ----

        public static void CreateEquipment(DbConnection connection, EquipmentDefinition equipment)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            InsertDefinition(connection, "equipment_definition", equipment.Id, equipment.Title, equipment.SortOrder,
                "Equipment", description: null, category: equipment.Category);
        }

        public static void UpdateEquipment(DbConnection connection, EquipmentDefinition equipment)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            Sql.Execute(connection, null,
                "UPDATE equipment_definition SET title = @title, category = @category, sort_order = @sort WHERE id = @id;",
                ("title", (object)equipment.Title ?? DBNull.Value), ("category", (object)equipment.Category ?? DBNull.Value),
                ("sort", equipment.SortOrder), ("id", equipment.Id));
        }

        public static List<EquipmentDefinition> ListEquipment(DbConnection connection)
        {
            var result = new List<EquipmentDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, title, category, sort_order FROM equipment_definition ORDER BY sort_order, title;",
                reader => result.Add(new EquipmentDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Category = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                }));
            return result;
        }

        public static int CountEquipmentUsage(DbConnection connection, string equipmentId)
        {
            return CountRelationUsage(connection, "card_required_equipment", "equipment_id", equipmentId);
        }

        // ---- Smart Toy capability definitions ----

        public static void CreateSmartToyCapability(DbConnection connection, SmartToyCapabilityDefinition capability)
        {
            if (capability == null) throw new ArgumentNullException(nameof(capability));
            InsertDefinition(connection, "smart_toy_capability_definition", capability.Id, capability.Title, capability.SortOrder,
                "Smart toy capability", description: null, category: capability.Category);
        }

        public static void UpdateSmartToyCapability(DbConnection connection, SmartToyCapabilityDefinition capability)
        {
            if (capability == null) throw new ArgumentNullException(nameof(capability));
            Sql.Execute(connection, null,
                "UPDATE smart_toy_capability_definition SET title = @title, category = @category, sort_order = @sort WHERE id = @id;",
                ("title", (object)capability.Title ?? DBNull.Value), ("category", (object)capability.Category ?? DBNull.Value),
                ("sort", capability.SortOrder), ("id", capability.Id));
        }

        public static List<SmartToyCapabilityDefinition> ListSmartToyCapabilities(DbConnection connection)
        {
            var result = new List<SmartToyCapabilityDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, title, category, sort_order FROM smart_toy_capability_definition ORDER BY sort_order, title;",
                reader => result.Add(new SmartToyCapabilityDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Category = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                }));
            return result;
        }

        public static int CountSmartToyCapabilityUsage(DbConnection connection, string capabilityId)
        {
            return GetSmartToyCapabilityUsage(connection, capabilityId).TotalReferences;
        }

        public static SmartToyCapabilityUsage GetSmartToyCapabilityUsage(DbConnection connection, string capabilityId)
        {
            var usage = new SmartToyCapabilityUsage { CapabilityId = capabilityId ?? "" };
            Sql.QueryAll(connection,
                "SELECT " +
                "(SELECT COUNT(*) FROM card_required_smart_toy_capability WHERE capability_id = @id), " +
                "(SELECT COUNT(*) FROM session_type_required_smart_toy_capability WHERE capability_id = @id), " +
                "(SELECT COUNT(*) FROM action_instance_toy_activity WHERE capability_id = @id), " +
                "(SELECT COUNT(*) FROM action_instance_toy_set_pattern WHERE capability_id = @id);",
                reader =>
                {
                    usage.Cards = reader.GetInt32(0);
                    usage.SessionTypes = reader.GetInt32(1);
                    usage.TimedToyPatternActions = reader.GetInt32(2);
                    usage.SetToyPatternActions = reader.GetInt32(3);
                },
                ("id", capabilityId));
            return usage;
        }

        public static void DeleteSmartToyCapabilityIfUnused(DbConnection connection, string capabilityId)
        {
            if (string.IsNullOrEmpty(capabilityId))
                throw new ArgumentException("Smart Toy Capability id required.", nameof(capabilityId));
            var usage = GetSmartToyCapabilityUsage(connection, capabilityId);
            if (usage.TotalReferences > 0)
            {
                throw new InvalidOperationException(
                    $"Smart Toy Capability '{capabilityId}' is referenced ({usage.Describe()}); delete blocked.");
            }
            Sql.Execute(connection, null,
                "DELETE FROM smart_toy_capability_definition WHERE id = @id;", ("id", capabilityId));
        }

        // ---- shared helpers ----

        private static void InsertDefinition(
            DbConnection connection, string table, string id, string title, int sortOrder,
            string label, string description, string category)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException(label + " id required.", nameof(id));

            string sql;
            if (table == "kink_definition")
            {
                sql = $"INSERT OR IGNORE INTO {table} (id, title, description, sort_order) VALUES (@id, @title, @desc, @sort);";
            }
            else if (table == "equipment_definition" || table == "smart_toy_capability_definition")
            {
                sql = $"INSERT OR IGNORE INTO {table} (id, title, category, sort_order) VALUES (@id, @title, @category, @sort);";
            }
            else
            {
                sql = $"INSERT OR IGNORE INTO {table} (id, title, sort_order) VALUES (@id, @title, @sort);";
            }

            Sql.Execute(connection, null, sql,
                ("id", id), ("title", (object)title ?? DBNull.Value), ("sort", sortOrder),
                ("desc", (object)description ?? DBNull.Value), ("category", (object)category ?? DBNull.Value));
        }

        private static void SetDefinitionSortOrder(DbConnection connection, string table, string id, int sortOrder)
        {
            Sql.Execute(connection, null,
                $"UPDATE {table} SET sort_order = @sort WHERE id = @id;",
                ("sort", sortOrder), ("id", id));
        }

        private static int CountRelationUsage(DbConnection connection, string table, string column, string id)
        {
            var count = 0;
            Sql.QueryAll(connection,
                $"SELECT COUNT(*) FROM {table} WHERE {column} = @id;",
                reader => count = reader.GetInt32(0),
                ("id", id));
            return count;
        }
    }

    public sealed class SmartToyCapabilityUsage
    {
        public string CapabilityId { get; set; } = "";
        public int Cards { get; set; }
        public int SessionTypes { get; set; }
        public int TimedToyPatternActions { get; set; }
        public int SetToyPatternActions { get; set; }
        public int TotalReferences => Cards + SessionTypes + TimedToyPatternActions + SetToyPatternActions;

        public string Describe()
        {
            return $"{Cards} Cards, {SessionTypes} Session Types, " +
                   $"{TimedToyPatternActions} Timed Toy Pattern Actions, " +
                   $"{SetToyPatternActions} Set Toy Pattern Actions";
        }
    }
}
