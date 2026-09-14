using System;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>A single ordered core schema migration.</summary>
    public sealed class CoreMigration
    {
        private readonly Lazy<string> _script;

        /// <summary>Scripted DDL-only migration.</summary>
        public CoreMigration(int version, string name, string scriptFileName)
            : this(version, name, scriptFileName, callback: null)
        {
        }

        /// <summary>
        /// Scripted DDL plus optional provider-neutral data-transformation code.
        /// The callback runs inside the SAME transaction, after every script
        /// statement, receiving the connection and that transaction. It must be
        /// written against <see cref="DbConnection"/>/<see cref="DbTransaction"/>
        /// only (no provider-specific API) so any host can run migrations.
        /// </summary>
        /// <param name="scriptFileName">
        /// Name of the canonical script under Assets/StreamingAssets/GameContentSchema.
        /// The text is resolved on first use through <see cref="SchemaScripts"/>,
        /// so declaring a migration never reads or embeds SQL itself.
        /// </param>
        public CoreMigration(int version, string name, string scriptFileName,
            Action<DbConnection, DbTransaction> callback)
        {
            if (string.IsNullOrEmpty(scriptFileName))
            {
                throw new ArgumentException("Schema script file name required.", nameof(scriptFileName));
            }

            Version = version;
            Name = name;
            ScriptFileName = scriptFileName;
            Callback = callback;
            _script = new Lazy<string>(() => SchemaScripts.Load(scriptFileName));
        }

        /// <summary>Monotonic version; the migration table's primary key.</summary>
        public int Version { get; }

        /// <summary>Human-readable migration name, recorded at apply time.</summary>
        public string Name { get; }

        /// <summary>Canonical script file name, as stored under Assets/StreamingAssets.</summary>
        public string ScriptFileName { get; }

        /// <summary>DDL script executed inside one transaction.</summary>
        public string Script
        {
            get { return _script.Value; }
        }

        /// <summary>Optional in-transaction data transformation; null for pure DDL.</summary>
        public Action<DbConnection, DbTransaction> Callback { get; }
    }
}
