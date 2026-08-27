using System;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// INTERIM Graph Workbench migration state (Docs/GraphWorkbench/
    /// 05-implementation-map.md): the portable snapshot became the v2 graph model
    /// in Ticket 02 while the database schema is still v1. Loading a faithful
    /// snapshot therefore requires BOTH the v2 schema (Ticket 03) and the sample
    /// transformation (Ticket 04); until then loading fails loudly instead of
    /// pretending old PhaseSlot-era rows can satisfy the new model.
    /// </summary>
    public static class GameContentSnapshotLoader
    {
        public static GameContentDefinition Load(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            throw new NotImplementedException(
                "The v2 snapshot loader lands in Docs/GraphWorkbench Ticket 04 " +
                "(schema migration 2 + canonical data transformation). The previous " +
                "PhaseSlot/top-level-action loader was removed with that obsolete " +
                "portable model.");
        }
    }
}
