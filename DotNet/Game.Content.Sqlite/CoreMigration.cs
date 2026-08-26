namespace TruthCardGame.Content.Sqlite
{
    /// <summary>A single ordered core schema migration.</summary>
    public sealed class CoreMigration
    {
        public CoreMigration(int version, string name, string script)
        {
            Version = version;
            Name = name;
            Script = script;
        }

        /// <summary>Monotonic version; the migration table's primary key.</summary>
        public int Version { get; }

        /// <summary>Human-readable migration name, recorded at apply time.</summary>
        public string Name { get; }

        /// <summary>DDL script executed inside one transaction.</summary>
        public string Script { get; }
    }
}
