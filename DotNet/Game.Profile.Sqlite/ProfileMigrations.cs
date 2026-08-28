using System;
using System.Collections.Generic;

namespace TruthCardGame.Profile.Sqlite
{
    /// <summary>One profile schema migration (DDL script + ledger row).</summary>
    public sealed class ProfileMigration
    {
        public ProfileMigration(int version, string name, string script)
        {
            Version = version;
            Name = name;
            Script = script;
        }

        public int Version { get; }
        public string Name { get; }
        public string Script { get; }
    }

    /// <summary>Ordered registry of profile schema migrations.</summary>
    public static class ProfileMigrations
    {
        public static int MaxVersion { get; } = 1;

        public static IReadOnlyList<ProfileMigration> All { get; } = new List<ProfileMigration>
        {
            new ProfileMigration(1, "profile-v1", LoadEmbeddedScript("PROFILE-SCHEMA-V1.sql")),
        };

        private static string LoadEmbeddedScript(string resourceName)
        {
            var fullName = typeof(ProfileMigrations).Namespace + "." + resourceName;
            var assembly = typeof(ProfileMigrations).Assembly;
            using (var stream = assembly.GetManifestResourceStream(fullName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded SQL resource not found: " + fullName);
                }

                using (var reader = new System.IO.StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
