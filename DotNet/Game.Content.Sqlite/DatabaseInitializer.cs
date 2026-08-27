using System;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// One-shot bootstrap: populates an EMPTY core database from a portable
    /// snapshot in a single transaction. INTERIM Graph Workbench migration state:
    /// seeding requires the v2 schema (Ticket 03) and the graph persistence
    /// implementation (Ticket 04); until then it fails loudly instead of writing
    /// half-truths. Host extension tables (unity_*, wpf_*) are never touched —
    /// that guarantee is re-proven by tests when the real writer lands.
    /// This is NOT the normal Save path; normal editing updates stable rows
    /// transactionally.
    /// </summary>
    public static class DatabaseInitializer
    {
        public static void InitializeEmptyDatabaseFromSnapshot(DbConnection connection, GameContentDefinition content)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (content == null) throw new ArgumentNullException(nameof(content));

            ConnectionInitializer.Initialize(connection);
            CoreMigrator.EnsureSchema(connection);

            throw new NotImplementedException(
                "Snapshot-to-DB seeding returns in Docs/GraphWorkbench Ticket 04 " +
                "with the v2 schema and graph repositories.");
        }
    }
}
