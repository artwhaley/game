using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content.Samples;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.Content.Sqlite.Tool
{
    /// <summary>
    /// Seeds the canonical Content/GameContent.db from SampleContent.
    /// Usage: dotnet run --project DotNet/Game.Content.Sqlite.Tool [--db &lt;path&gt;]
    /// Default path is Content/GameContent.db relative to the working directory.
    /// Refuses to touch a database that already has core content.
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
                    DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(connection, SampleContent.Build());
                    Console.WriteLine($"Seeded '{fullPath}' (core schema v{version}) from SampleContent.");
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
