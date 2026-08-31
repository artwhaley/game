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
    /// physically present but unused (see the v2 script header). v4 changes
    /// PhaseGoto assignment to nullable so Unassigned is a first-class state.
    /// </summary>
    public static class CoreMigrations
    {
        /// <summary>Highest applied schema version (the last registered migration).</summary>
        public static int MaxVersion
        {
            get
            {
                var max = 0;
                foreach (var migration in All)
                {
                    if (migration.Version > max) max = migration.Version;
                }
                return max;
            }
        }

        public static IReadOnlyList<CoreMigration> All { get; } = new List<CoreMigration>
        {
            new CoreMigration(1, "core-schema-v1", LoadEmbeddedScript("SQLITE-SCHEMA-V1.sql")),
            new CoreMigration(2, "core-graph-schema-v2",
                LoadEmbeddedScript("SQLITE-SCHEMA-V2.sql"),
                Migration2Transform.Transform),
            new CoreMigration(3, "wpf-authoring-layout",
                LoadEmbeddedScript("SQLITE-SCHEMA-V3-WPF-AUTHORING.sql")),
            new CoreMigration(4, "phase-goto-nullable-exit",
                LoadEmbeddedScript("SQLITE-SCHEMA-V4-PHASE-GOTO-NULL.sql")),
            new CoreMigration(5, "milestone-b-cards-profile-selection",
                LoadEmbeddedScript("SQLITE-SCHEMA-V5-MILESTONE-B.sql"),
                Migration5Transform.Transform),
            new CoreMigration(6, "dialog-delay-toy-actions",
                LoadEmbeddedScript("SQLITE-SCHEMA-V6-ACTIONS.sql")),
            new CoreMigration(7, "card-folders",
                LoadEmbeddedScript("SQLITE-SCHEMA-V7-CARD-FOLDERS.sql")),
            new CoreMigration(8, "toy-pattern-dialog-catalog",
                LoadEmbeddedScript("SQLITE-SCHEMA-V8-TOY-DIALOG.sql"),
                Migration8Transform.Transform),
            new CoreMigration(9, "card-folder-hierarchy",
                LoadEmbeddedScript("SQLITE-SCHEMA-V9-CARD-FOLDER-HIERARCHY.sql"),
                Migration9Transform.Transform)
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
