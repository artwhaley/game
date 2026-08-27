using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content.Samples;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.Content.Sqlite.Tool
{
    /// <summary>
    /// Seeds the canonical Content/GameContent.db from SampleContent (schema v2),
    /// or upgrades an existing core database in place with --migrate.
    /// Usage: dotnet run --project DotNet/Game.Content.Sqlite.Tool [--db &lt;path&gt;] [--migrate]
    /// Default path is Content/GameContent.db relative to the working directory.
    /// Seeding refuses to touch a database that already has core content.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                var dbPath = DefaultDbPath();
                var migrateOnly = false;
                for (var i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--db" && i + 1 < args.Length) dbPath = args[i + 1];
                    if (args[i] == "--migrate") migrateOnly = true;
                }

                var fullPath = Path.GetFullPath(dbPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (var connection = new SqliteConnection("Data Source=" + fullPath))
                {
                    ConnectionInitializer.Initialize(connection);
                    if (migrateOnly)
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

        private static string DefaultDbPath()
        {
            return Path.Combine("Content", "GameContent.db");
        }
    }
}
