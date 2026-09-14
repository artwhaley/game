using System;
using System.Collections.Generic;

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
    /// v10 adds editor-only WPF Action Block templates; runtime action models
    /// remain unchanged. v11 adds WPF graph portals. v12 adds the Conversation
    /// Performance vocabulary (Performance Tags, Performance Events and the
    /// Perform action subtype).
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

        /// <summary>
        /// Registered migrations, built on first use. Lazy on purpose: reading
        /// <see cref="MaxVersion"/> must not require the scripts to be readable,
        /// because a read-only host (Unity) checks the stored version without
        /// ever applying a migration.
        /// </summary>
        public static IReadOnlyList<CoreMigration> All
        {
            get { return LazyAll.Value; }
        }

        private static readonly Lazy<IReadOnlyList<CoreMigration>> LazyAll =
            new Lazy<IReadOnlyList<CoreMigration>>(CreateAll);

        private static IReadOnlyList<CoreMigration> CreateAll()
        {
            return new List<CoreMigration>
            {
                new CoreMigration(1, "core-schema-v1", "SQLITE-SCHEMA-V1.sql"),
                new CoreMigration(2, "core-graph-schema-v2", "SQLITE-SCHEMA-V2.sql",
                    Migration2Transform.Transform),
                new CoreMigration(3, "wpf-authoring-layout", "SQLITE-SCHEMA-V3-WPF-AUTHORING.sql"),
                new CoreMigration(4, "phase-goto-nullable-exit", "SQLITE-SCHEMA-V4-PHASE-GOTO-NULL.sql"),
                new CoreMigration(5, "milestone-b-cards-profile-selection", "SQLITE-SCHEMA-V5-MILESTONE-B.sql",
                    Migration5Transform.Transform),
                new CoreMigration(6, "dialog-delay-toy-actions", "SQLITE-SCHEMA-V6-ACTIONS.sql"),
                new CoreMigration(7, "card-folders", "SQLITE-SCHEMA-V7-CARD-FOLDERS.sql"),
                new CoreMigration(8, "toy-pattern-dialog-catalog", "SQLITE-SCHEMA-V8-TOY-DIALOG.sql",
                    Migration8Transform.Transform),
                new CoreMigration(9, "card-folder-hierarchy", "SQLITE-SCHEMA-V9-CARD-FOLDER-HIERARCHY.sql",
                    Migration9Transform.Transform),
                new CoreMigration(10, "wpf-action-block-templates", "SQLITE-SCHEMA-V10-ACTION-BLOCKS.sql"),
                new CoreMigration(11, "wpf-graph-portal-pairs", "SQLITE-SCHEMA-V11-WPF-GRAPH-PORTALS.sql"),
                new CoreMigration(12, "conversation-performance", "SQLITE-SCHEMA-V12-PERFORMANCE.sql"),
            };
        }
    }
}
