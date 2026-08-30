using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 09: granular repository for portable Resources (identity only:
    /// Id + Kind + Name). Delete safety: a Resource referenced by a Cutscene,
    /// Timed Toy Pattern, or Set Toy Pattern action cannot be deleted — the
    /// caller receives the usage details instead. Kind is immutable after
    /// creation. Unknown kinds remain safe (loaded, listed, never validated
    /// away by this layer).
    /// </summary>
    public static class ResourceRepository
    {
        public static void Create(DbConnection connection, ResourceDefinition resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (string.IsNullOrEmpty(resource.Id)) throw new ArgumentException("Resource id required.", nameof(resource.Id));
            if (string.IsNullOrEmpty(resource.Kind)) throw new ArgumentException("Resource kind required.", nameof(resource.Kind));

            Sql.Execute(connection, null,
                "INSERT OR IGNORE INTO resource (id, kind, name) VALUES (@id, @kind, @name);",
                ("id", resource.Id), ("kind", resource.Kind),
                ("name", (object)resource.Name ?? DBNull.Value));
        }

        public static void Rename(DbConnection connection, string id, string name)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Resource id required.", nameof(id));
            Sql.Execute(connection, null,
                "UPDATE resource SET name = @name WHERE id = @id;",
                ("name", (object)name ?? DBNull.Value), ("id", id));
        }

        public static ResourceDefinition Get(DbConnection connection, string id)
        {
            ResourceDefinition result = null;
            Sql.QueryAll(connection,
                "SELECT id, kind, name FROM resource WHERE id = @id;",
                reader => result = new ResourceDefinition
                {
                    Id = reader.GetString(0),
                    Kind = reader.GetString(1),
                    Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                },
                ("id", id));
            return result;
        }

        public static List<ResourceDefinition> List(DbConnection connection)
        {
            var result = new List<ResourceDefinition>();
            Sql.QueryAll(connection, "SELECT id, kind, name FROM resource ORDER BY id;",
                reader => result.Add(new ResourceDefinition
                {
                    Id = reader.GetString(0),
                    Kind = reader.GetString(1),
                    Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                }));
            return result;
        }

        /// <summary>How many authored actions reference this Resource, per consumer.</summary>
        public static ResourceUsage GetUsage(DbConnection connection, string resourceId)
        {
            var usage = new ResourceUsage { ResourceId = resourceId };
            Sql.QueryAll(connection,
                "SELECT " +
                "(SELECT COUNT(*) FROM action_instance_cutscene WHERE resource_id = @id) + " +
                "(SELECT COUNT(*) FROM action_instance_toy_activity WHERE pattern_resource_id = @id) + " +
                "(SELECT COUNT(*) FROM action_instance_toy_set_pattern WHERE pattern_resource_id = @id);",
                reader => usage.TotalReferences = reader.GetInt32(0),
                ("id", resourceId));
            return usage;
        }

        /// <summary>
        /// Deletes an unused Resource. Referenced Resources are refused with a
        /// readable error; no cascade over Actions ever happens.
        /// </summary>
        public static void DeleteIfUnused(DbConnection connection, string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId)) throw new ArgumentException("Resource id required.", nameof(resourceId));
            var usage = GetUsage(connection, resourceId);
            if (usage.TotalReferences > 0)
            {
                throw new InvalidOperationException(
                    $"Resource '{resourceId}' is referenced by {usage.TotalReferences} action(s) " +
                    "(cutscene / timed toy pattern / set toy pattern); delete blocked.");
            }
            Sql.Execute(connection, null, "DELETE FROM resource WHERE id = @id;", ("id", resourceId));
        }
    }

    /// <summary>Usage summary for one Resource (Ticket 09 delete safety).</summary>
    public sealed class ResourceUsage
    {
        public string ResourceId { get; set; } = "";
        public int TotalReferences { get; set; }
    }
}
