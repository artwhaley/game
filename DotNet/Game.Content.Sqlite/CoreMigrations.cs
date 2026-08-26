using System;
using System.Collections.Generic;
using System.IO;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ordered registry of core schema migrations. v1 is the initial core
    /// schema, embedded as an assembly resource so it ships with the library.
    /// </summary>
    public static class CoreMigrations
    {
        public static IReadOnlyList<CoreMigration> All { get; } = new List<CoreMigration>
        {
            new CoreMigration(1, "core-schema-v1", LoadEmbeddedScript("SQLITE-SCHEMA-V1.sql"))
        };

        private static string LoadEmbeddedScript(string resourceName)
        {
            var fullName = typeof(CoreMigrations).Namespace + "." + resourceName;
            var assembly = typeof(CoreMigrations).Assembly;
            using (var stream = assembly.GetManifestResourceStream(fullName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded SQL resource not found: " + fullName);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
