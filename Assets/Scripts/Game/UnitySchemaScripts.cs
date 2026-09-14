using System;
using System.IO;
using UnityEngine;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame
{
    /// <summary>
    /// Installs the Unity-side reader for the canonical core schema scripts.
    ///
    /// <see cref="SchemaScripts"/> deliberately holds no Unity reference — the
    /// same source compiles into the DotNet assembly, which embeds the scripts as
    /// resources. Unity compiles that source without any embedded resources, so
    /// the scripts have to come from somewhere Unity can actually read raw files:
    /// <c>Assets/StreamingAssets/GameContentSchema</c>, which Unity copies
    /// verbatim at edit time and into a build.
    ///
    /// This lives in the Unity host assembly, not in shared source, exactly so
    /// the portable mapping keeps compiling under DotNet.
    ///
    /// Migrations are WPF's job in normal operation: Unity reads an
    /// already-migrated database with <c>ensureSchema: false</c>. Installing this
    /// reader is what lets Unity apply or verify the schema when it genuinely
    /// needs to (a disposable copy, a fresh machine, a verification run) instead
    /// of failing with a missing-resource error.
    ///
    /// Transport note: <c>File</c> access works in the editor and in desktop
    /// players. Android/iOS builds need UnityWebRequest/StreamingAssets
    /// handling; that is deliberately not built here because V1's Unity path is
    /// an editor-hosted session.
    /// </summary>
    public static class UnitySchemaScripts
    {
        public const string ScriptFolderName = "GameContentSchema";

        /// <summary>Absolute path to the schema script folder for this host.</summary>
        public static string ScriptDirectory
        {
            get { return Path.Combine(Application.streamingAssetsPath, ScriptFolderName); }
        }

        /// <summary>Installs the StreamingAssets reader. Safe to call repeatedly.</summary>
        public static void Install()
        {
            SchemaScripts.Reader = ReadScript;
        }

        /// <summary>Removes any installed reader, restoring the shared default.</summary>
        public static void Uninstall()
        {
            SchemaScripts.Reader = null;
        }

        /// <summary>
        /// Reads one schema script from StreamingAssets. Throws with the resolved
        /// path rather than returning null, so a packaging mistake reads as a
        /// packaging mistake instead of as an empty migration.
        /// </summary>
        private static string ReadScript(string scriptFileName)
        {
            var path = Path.Combine(ScriptDirectory, scriptFileName);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    "UnitySchemaScripts: schema script not found at '" + path + "'. " +
                    "The canonical scripts live in Assets/StreamingAssets/" + ScriptFolderName + ".");
            }

            return File.ReadAllText(path);
        }
    }
}
