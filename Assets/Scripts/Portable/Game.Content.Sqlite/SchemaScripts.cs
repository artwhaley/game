using System;
using System.IO;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Resolves the canonical core schema scripts.
    ///
    /// The scripts live in exactly one place — the raw files under
    /// <c>Assets/StreamingAssets/GameContentSchema</c> — and two hosts read that
    /// one copy in two different ways:
    ///
    ///   * the DotNet build embeds them as assembly resources (see
    ///     Game.Content.Sqlite.csproj), which is the default path here;
    ///   * Unity compiles this same source into an assembly that has no embedded
    ///     resources at all, so a Unity host installs <see cref="Reader"/> and
    ///     reads the same files from <c>Application.streamingAssetsPath</c>
    ///     (see TruthCardGame.UnitySchemaScripts).
    ///
    /// Resolution is deliberately loud rather than clever. An installed reader
    /// wins, the embedded default is tried next, and anything else throws with
    /// the concrete remedy. There is no path that quietly returns an empty or
    /// truncated script, because an empty script would still "succeed" and leave
    /// a database half-created.
    /// </summary>
    public static class SchemaScripts
    {
        /// <summary>
        /// Host-supplied script reader, given the script file name
        /// (e.g. <c>SQLITE-SCHEMA-V11-WPF-GRAPH-PORTALS.sql</c>) and returning its
        /// full text. Unity hosts must install this before any migration runs.
        /// </summary>
        public static Func<string, string> Reader { get; set; }

        /// <summary>
        /// Loads one schema script by file name, or throws with the exact remedy.
        /// </summary>
        public static string Load(string scriptFileName)
        {
            if (string.IsNullOrEmpty(scriptFileName))
            {
                throw new ArgumentException("Schema script file name required.", nameof(scriptFileName));
            }

            var reader = Reader;
            if (reader != null)
            {
                var supplied = reader(scriptFileName);
                if (string.IsNullOrEmpty(supplied))
                {
                    throw new InvalidOperationException(
                        "SchemaScripts: the installed reader returned no text for '" + scriptFileName + "'.");
                }
                return supplied;
            }

            var embedded = LoadEmbedded(scriptFileName);
            if (embedded != null) return embedded;

            throw new InvalidOperationException(
                "SchemaScripts: cannot read '" + scriptFileName + "'. This assembly embeds no schema " +
                "resources — which is expected under Unity, where the same source compiles without them. " +
                "Install SchemaScripts.Reader from the canonical files under " +
                "Assets/StreamingAssets/GameContentSchema (Unity: TruthCardGame.UnitySchemaScripts.Install).");
        }

        /// <summary>
        /// Embedded-resource lookup, tolerant of how MSBuild derives manifest
        /// names for files included from outside the project directory. Returns
        /// null when this assembly embeds nothing for the script.
        /// </summary>
        private static string LoadEmbedded(string scriptFileName)
        {
            var assembly = typeof(SchemaScripts).Assembly;

            string match = null;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(scriptFileName, StringComparison.Ordinal)) continue;
                if (match != null)
                {
                    throw new InvalidOperationException(
                        "SchemaScripts: more than one embedded resource ends with '" + scriptFileName +
                        "': '" + match + "' and '" + name + "'.");
                }
                match = name;
            }

            if (match == null) return null;

            using (var stream = assembly.GetManifestResourceStream(match))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
