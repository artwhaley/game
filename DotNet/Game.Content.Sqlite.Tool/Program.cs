using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.Content.Sqlite.Tool
{
    /// <summary>
    /// Seeds the canonical Content/GameContent.db from SampleContent.
    /// Usage: dotnet run --project DotNet/Game.Content.Sqlite.Tool [--db &lt;path&gt;]
    ///
    /// INTERIM Graph Workbench migration state: with the portable model on the v2
    /// graph shape but the database still at schema v1, this tool runs migrations
    /// only and reports that seeding returns in Ticket 04
    /// (Docs/GraphWorkbench/05-implementation-map.md).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                var dbPath = DefaultDbPath();
                for (var i = 0; i + 1 < args.Length; i++)
                {
                    if (args[i] == "--db") dbPath = args[i + 1];
                }

                var fullPath = Path.GetFullPath(dbPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (var connection = new SqliteConnection("Data Source=" + fullPath))
                {
                    ConnectionInitializer.Initialize(connection);
                    var version = CoreMigrator.EnsureSchema(connection);
                    Console.WriteLine(
                        $"Ensured core schema v{version} on '{fullPath}'. " +
                        "Snapshot seeding is suspended until Graph Workbench Ticket 04 lands " +
                        "(v2 schema migration + graph persistence).");
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
