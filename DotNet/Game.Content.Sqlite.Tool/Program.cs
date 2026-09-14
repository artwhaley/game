using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.Content.Sqlite.Tool
{
    /// <summary>
    /// Seeds the canonical Content/GameContent.db from SampleContent (schema v2),
    /// upgrades an existing core database in place with --migrate, or creates the
    /// V1 dev-fixture Performance Tag vocabulary with --seed-performance-tags.
    /// Usage: dotnet run --project DotNet/Game.Content.Sqlite.Tool [--db &lt;path&gt;] [--migrate] [--seed-performance-tags]
    /// Default path is Content/GameContent.db relative to the working directory.
    /// Seeding refuses to touch a database that already has core content.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// V1 dev-fixture Performance Tag vocabulary, mirroring
        /// PerformanceCatalogSetup.SuggestedTagTitles in Unity. These are titles
        /// for a human; the minted opaque IDs are what Unity ingredient
        /// membership stores. The vocabulary itself is WPF/SQLite-owned, so this
        /// command exists only so a developer (or a fresh checkout) can reach a
        /// working picker without hand-authoring, and it is a no-op once the
        /// titles exist.
        /// </summary>
        private static readonly string[] V1PerformanceTagTitles =
        {
            "Playful",
            "Tease",
            "Stern",
            "Comforting",
        };

        public static int Main(string[] args)
        {
            try
            {
                var dbPath = DefaultDbPath();
                var migrateOnly = false;
                var seedPerformanceTags = false;
                for (var i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--db" && i + 1 < args.Length) dbPath = args[i + 1];
                    if (args[i] == "--migrate") migrateOnly = true;
                    if (args[i] == "--seed-performance-tags") seedPerformanceTags = true;
                }

                var fullPath = Path.GetFullPath(dbPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (var connection = new SqliteConnection("Data Source=" + fullPath))
                {
                    ConnectionInitializer.Initialize(connection);
                    if (seedPerformanceTags)
                    {
                        var version = CoreMigrator.EnsureSchema(connection);
                        var created = SeedPerformanceTags(connection);
                        Console.WriteLine(created == 0
                            ? $"Performance Tag vocabulary already present in '{fullPath}' (core schema v{version})."
                            : $"Created {created} Performance Tag(s) in '{fullPath}' (core schema v{version}).");
                    }
                    else if (migrateOnly)
                    {
                        var version = CoreMigrator.EnsureSchema(connection);
                        Console.WriteLine($"Migrated '{fullPath}' in place (core schema v{version}).");
                    }
                    else
                    {
                        DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(connection, SampleContent.Create());
                        var version = CoreMigrator.EnsureSchema(connection);
                        Console.WriteLine($"Seeded '{fullPath}' (core schema v{version}) from SampleContent.");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Creates any missing fixture tag and returns how many were added.
        /// Matching is by title, so re-running never mints a second ID for a
        /// tag that already exists.
        /// </summary>
        private static int SeedPerformanceTags(DbConnection connection)
        {
            var tags = PerformanceCatalogRepository.ListTags(connection);
            var existingTitles = new HashSet<string>(
                tags.Select(tag => tag.Title ?? string.Empty), StringComparer.Ordinal);
            var nextSortOrder = tags.Count;

            var created = 0;
            foreach (var title in V1PerformanceTagTitles)
            {
                if (!existingTitles.Add(title)) continue;
                PerformanceCatalogRepository.CreateTag(connection, new PerformanceTagDefinition
                {
                    Id = StableIds.New(),
                    Title = title,
                    SortOrder = nextSortOrder++,
                });
                created++;
            }
            return created;
        }

        private static string DefaultDbPath()
        {
            return Path.Combine("Content", "GameContent.db");
        }
    }
}
