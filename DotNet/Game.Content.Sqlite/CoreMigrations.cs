using System;
using System.Collections.Generic;
using System.IO;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ordered registry of core schema migrations.
    ///
    /// v1 is the initial core schema (PhaseSlot-era). v2 introduces the Graph
    /// Workbench structures (Docs/GraphWorkbench/03-schema-v2-design.md) plus its
    /// in-transaction legacy-content transformation; obsolete v1 tables remain
    /// physically present but unused (see the v2 script header).
    /// </summary>
    public static class CoreMigrations
    {
        public static IReadOnlyList<CoreMigration> All { get; } = new List<CoreMigration>
        {
            new CoreMigration(1, "core-schema-v1", LoadEmbeddedScript("SQLITE-SCHEMA-V1.sql")),
            new CoreMigration(2, "core-graph-schema-v2",
                LoadEmbeddedScript("SQLITE-SCHEMA-V2.sql"),
                Migration2Transform.Transform)
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
