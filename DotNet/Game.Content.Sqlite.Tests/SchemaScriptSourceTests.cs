using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 00 source-arrangement guard.
    ///
    /// The core schema scripts now live in exactly one place —
    /// Assets/StreamingAssets/GameContentSchema, the only location Unity can read
    /// raw files from — and the DotNet build embeds that same copy. These tests
    /// fail if the two ever drift apart, or if the embedded lookup stops finding
    /// a script, so the "one copy, two readers" arrangement is verified rather
    /// than asserted in a comment.
    /// </summary>
    [TestFixture]
    public class SchemaScriptSourceTests
    {
        [Test]
        public void EveryRegisteredMigration_ResolvesANonEmptyScript()
        {
            Assert.AreEqual(11, CoreMigrations.All.Count, "registered migrations");
            Assert.AreEqual(11, CoreMigrations.MaxVersion, "highest schema version");

            foreach (var migration in CoreMigrations.All)
            {
                Assert.That(migration.Script, Is.Not.Null.And.Not.Empty,
                    $"migration {migration.Version} ({migration.Name}) resolved a script");
                Assert.That(migration.Script, Does.Contain(";"),
                    $"migration {migration.Version} ({migration.Name}) looks like SQL");
            }
        }

        [Test]
        public void EmbeddedScripts_MatchTheCanonicalStreamingAssetsCopy()
        {
            foreach (var migration in CoreMigrations.All)
            {
                var filePath = Path.Combine(SchemaScriptDirectory(), migration.ScriptFileName);
                Assert.That(File.Exists(filePath), Is.True,
                    "canonical schema script missing: " + filePath);

                var onDisk = Normalize(File.ReadAllText(filePath));
                var embedded = Normalize(migration.Script);

                Assert.AreEqual(onDisk, embedded,
                    $"embedded SQL for {migration.ScriptFileName} no longer matches the canonical file under Assets/StreamingAssets");
            }
        }

        [Test]
        public void SchemaScriptDirectory_HoldsExactlyTheRegisteredScripts()
        {
            var registered = new List<string>();
            foreach (var migration in CoreMigrations.All) registered.Add(migration.ScriptFileName);

            var onDisk = new List<string>();
            foreach (var path in Directory.GetFiles(SchemaScriptDirectory(), "*.sql"))
            {
                onDisk.Add(Path.GetFileName(path));
            }

            onDisk.Sort(StringComparer.Ordinal);
            registered.Sort(StringComparer.Ordinal);

            CollectionAssert.AreEqual(registered, onDisk,
                "Assets/StreamingAssets/GameContentSchema must hold exactly the scripts CoreMigrations registers");
        }

        [Test]
        public void MissingScript_ThrowsWithTheRemedyNamed()
        {
            // A host that cannot resolve a script must never get an empty string
            // back: an empty script would still "apply" and leave a half-created
            // database. This pins the loud-failure contract.
            var installed = SchemaScripts.Reader;
            try
            {
                SchemaScripts.Reader = null;
                var exception = Assert.Throws<InvalidOperationException>(
                    () => SchemaScripts.Load("SQLITE-SCHEMA-NOT-REGISTERED.sql"));
                Assert.That(exception.Message, Does.Contain("SQLITE-SCHEMA-NOT-REGISTERED.sql"));
            }
            finally
            {
                SchemaScripts.Reader = installed;
            }
        }

        [Test]
        public void InstalledReader_WinsOverEmbeddedResources()
        {
            var installed = SchemaScripts.Reader;
            try
            {
                SchemaScripts.Reader = name => "-- supplied by a host reader: " + name;
                Assert.AreEqual("-- supplied by a host reader: SQLITE-SCHEMA-V1.sql",
                    SchemaScripts.Load("SQLITE-SCHEMA-V1.sql"));
            }
            finally
            {
                SchemaScripts.Reader = installed;
            }
        }

        private static string SchemaScriptDirectory()
        {
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "Assets", "StreamingAssets", "GameContentSchema"));
        }

        /// <summary>Line-ending-insensitive comparison: git and Unity may rewrite them.</summary>
        private static string Normalize(string text)
        {
            return text == null ? null : text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
        }
    }
}
