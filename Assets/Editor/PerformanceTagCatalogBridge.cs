using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Performance;
using UnityEngine;

namespace TruthCardGame.EditorTools
{
    /// <summary>One Performance Tag as read from the content database.</summary>
    public sealed class PerformanceTagInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsRetired { get; set; }

        public string DisplayName => IsRetired
            ? (string.IsNullOrEmpty(Title) ? Id : Title) + " (retired)"
            : (string.IsNullOrEmpty(Title) ? Id : Title);
    }

    /// <summary>
    /// Bridge between the Unity-owned ingredient registry and the WPF-owned
    /// Performance Tag vocabulary in the canonical SQLite database.
    ///
    /// Unity never copies tag titles. It reads stable IDs (and titles only for
    /// display), so a rename in WPF shows up here on the next refresh without any
    /// content migration, and an ingredient that references an unknown or
    /// retired tag is reported rather than silently accepted.
    ///
    /// The database is opened read-only: the canonical store is authored by the
    /// Workbench, and a Unity write would be a bug, not a feature.
    /// </summary>
    public static class PerformanceTagCatalogBridge
    {
        private static List<PerformanceTagInfo> _cachedTags = new List<PerformanceTagInfo>();
        private static DateTime _loadedAtUtc = DateTime.MinValue;
        private static string _loadedFrom = "";

        /// <summary>Canonical authored content database, at the repository root.</summary>
        public static string CanonicalDatabasePath =>
            Path.Combine(ProjectRoot, "Content", "GameContent.db");

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static IReadOnlyList<PerformanceTagInfo> CachedTags => _cachedTags;

        public static DateTime LoadedAtUtc => _loadedAtUtc;

        public static string LoadedFrom => _loadedFrom;

        /// <summary>
        /// Re-reads the tag vocabulary. Failure is never fatal to authoring: an
        /// unreachable database yields an empty catalog plus a reason, because a
        /// missing Workbench save must not block the Unity artist.
        /// </summary>
        public static bool Refresh(out string message)
        {
            return RefreshFrom(CanonicalDatabasePath, out message);
        }

        public static bool RefreshFrom(string databasePath, out string message)
        {
            try
            {
                _cachedTags = ReadTags(databasePath);
                _loadedAtUtc = DateTime.UtcNow;
                _loadedFrom = databasePath ?? "";
                message = $"Loaded {_cachedTags.Count} Performance Tag(s) from {_loadedFrom}.";
                return true;
            }
            catch (Exception ex)
            {
                _cachedTags = new List<PerformanceTagInfo>();
                _loadedFrom = databasePath ?? "";
                message = "Performance Tag vocabulary unavailable: " + ex.Message;
                return false;
            }
        }

        /// <summary>Reads the tag rows from a database file without mutating it.</summary>
        public static List<PerformanceTagInfo> ReadTags(string databasePath)
        {
            var result = new List<PerformanceTagInfo>();
            if (string.IsNullOrEmpty(databasePath) || !File.Exists(databasePath)) return result;

            // We vendor Microsoft.Data.Sqlite.Core, so nothing initializes
            // SQLitePCLRaw on our behalf.
            Batteries_V2.Init();

            using (var connection = new SqliteConnection(
                       "Data Source=" + databasePath + ";Mode=ReadOnly;Pooling=False"))
            {
                connection.Open();
                if (!TableExists(connection, "performance_tag_definition")) return result;

                foreach (var tag in PerformanceCatalogRepository.ListTags(connection))
                {
                    result.Add(new PerformanceTagInfo
                    {
                        Id = tag.Id,
                        Title = tag.Title,
                        IsRetired = tag.IsRetired,
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Reports ingredient tag references that no longer resolve: unknown IDs,
        /// retired tags still in use, and empty memberships on enabled acting.
        /// </summary>
        public static List<string> ValidateRegistryTags(
            PerformanceRegistry registry, IReadOnlyList<PerformanceTagInfo> tags)
        {
            var problems = new List<string>();
            if (registry == null) return problems;

            var known = new Dictionary<string, PerformanceTagInfo>(StringComparer.Ordinal);
            foreach (var tag in tags ?? new List<PerformanceTagInfo>())
            {
                if (tag != null && !string.IsNullOrEmpty(tag.Id) && !known.ContainsKey(tag.Id))
                    known.Add(tag.Id, tag);
            }

            foreach (var ingredient in registry.Ingredients)
            {
                if (ingredient == null) continue;
                var label = ingredient.DisplayName;
                if (ingredient.PerformanceTagIds.Count == 0)
                {
                    if (ingredient.Enabled && ingredient.Kind != PresentationIngredientKinds.Foundation)
                    {
                        problems.Add($"'{label}' has no Performance Tag, so no event can select it.");
                    }
                    continue;
                }

                foreach (var tagId in ingredient.PerformanceTagIds)
                {
                    if (!known.TryGetValue(tagId, out var tag))
                    {
                        problems.Add($"'{label}' references unknown Performance Tag '{tagId}'.");
                    }
                    else if (tag.IsRetired)
                    {
                        problems.Add($"'{label}' references retired Performance Tag '{tag.DisplayName}'.");
                    }
                }
            }
            return problems;
        }

        private static bool TableExists(System.Data.Common.DbConnection connection, string name)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = @n;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@n";
                parameter.Value = name;
                command.Parameters.Add(parameter);
                return Convert.ToInt64(command.ExecuteScalar()) > 0;
            }
        }
    }
}
