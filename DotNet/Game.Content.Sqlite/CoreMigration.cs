using System;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>A single ordered core schema migration.</summary>
    public sealed class CoreMigration
    {
        /// <summary>Scripted DDL-only migration.</summary>
        public CoreMigration(int version, string name, string script)
            : this(version, name, script, callback: null)
        {
        }

        /// <summary>
        /// Scripted DDL plus optional provider-neutral data-transformation code.
        /// The callback runs inside the SAME transaction, after every script
        /// statement, receiving the connection and that transaction. It must be
        /// written against <see cref="DbConnection"/>/<see cref="DbTransaction"/>
        /// only (no provider-specific API) so any host can run migrations.
        /// </summary>
        public CoreMigration(int version, string name, string script, Action<DbConnection, DbTransaction> callback)
        {
            Version = version;
            Name = name;
            Script = script;
            Callback = callback;
        }

        /// <summary>Monotonic version; the migration table's primary key.</summary>
        public int Version { get; }

        /// <summary>Human-readable migration name, recorded at apply time.</summary>
        public string Name { get; }

        /// <summary>DDL script executed inside one transaction.</summary>
        public string Script { get; }

        /// <summary>Optional in-transaction data transformation; null for pure DDL.</summary>
        public Action<DbConnection, DbTransaction> Callback { get; }
    }
}
