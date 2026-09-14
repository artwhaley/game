using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using SQLitePCL;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// Ticket 00 integration acceptance: the real content path runs inside the
    /// pinned Unity editor, and there is exactly one CLR identity per portable
    /// type.
    ///
    /// The shared mapping source now lives under Assets/Scripts/Portable like
    /// Game.Content/Game.Core, so Unity compiles GameContentSnapshotLoader from
    /// the same files the DotNet build links. These tests prove that arrangement
    /// rather than assuming it:
    ///
    ///   * the loader reads a real migrated database in Unity;
    ///   * the snapshot it returns is handed straight into the engine Core that
    ///     Unity compiles, and the run advances;
    ///   * <see cref="GameContentDefinition"/> is one type — the loader's
    ///     declared return type, the engine's constructor parameter and
    ///     <c>typeof(GameContentDefinition)</c> are the same Type, and no second
    ///     definition of any TruthCardGame.Content/Core type is loaded;
    ///   * the schema scripts read correctly from StreamingAssets (Unity cannot
    ///     read the embedded resources the DotNet build uses), a missing reader
    ///     fails loudly, and version inspection never needs a script at all.
    ///
    /// Scope note: this proves the content path, not presentation. Nothing here
    /// renders anything; ticket 04 owns the visual run.
    /// </summary>
    public class SqliteContentIntegrationTests
    {
        private sealed class RecordingLog : IGameLog
        {
            public readonly List<string> Entries = new List<string>();
            public void Info(string message) { Entries.Add("info:" + message); }
            public void Warning(string message) { Entries.Add("warn:" + message); }
            public void Error(string message) { Entries.Add("error:" + message); }

            public bool HasErrors
            {
                get
                {
                    foreach (var entry in Entries) if (entry.StartsWith("error:", StringComparison.Ordinal)) return true;
                    return false;
                }
            }
        }

        /// <summary>Completes immediately: this test measures the content path, not timing.</summary>
        private sealed class ImmediateDelay : IGameDelay
        {
            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private static string RepositoryRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string CanonicalDatabasePath =>
            Path.Combine(RepositoryRoot, "Content", "GameContent.db");

        /// <summary>
        /// The preserved v1-era fixture. The canonical database is a valid but
        /// currently content-empty authoring store, so real content comes from
        /// migrating this fixture — which also exercises the schema scripts.
        /// </summary>
        private static string LegacyFixturePath =>
            Path.Combine(RepositoryRoot, "DotNet", "Game.Content.Sqlite.Tests", "Fixtures", "GameContent-v1.db");

        private static string SchemaScriptDirectory =>
            Path.Combine(RepositoryRoot, "Assets", "StreamingAssets", UnitySchemaScripts.ScriptFolderName);

        [SetUp]
        public void SetUp()
        {
            Batteries_V2.Init();
            UnitySchemaScripts.Install();
        }

        [TearDown]
        public void TearDown()
        {
            UnitySchemaScripts.Uninstall();
            SqliteConnection.ClearAllPools();
        }

        // ---------- the canonical database, read-only ----------

        [Test]
        public void CanonicalDatabase_LoadsThroughTheSharedLoader_ReadOnly()
        {
            Assert.That(File.Exists(CanonicalDatabasePath), Is.True,
                "canonical content database not found at " + CanonicalDatabasePath);

            GameContentDefinition content;
            using (var connection = OpenReadOnly(CanonicalDatabasePath))
            {
                content = GameContentSnapshotLoader.Load(connection, ensureSchema: false);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE unity_must_not_write (id TEXT);";
                    Assert.Throws<SqliteException>(() => command.ExecuteNonQuery(),
                        "the canonical database must reject writes on this connection");
                }
            }

            Assert.That(content, Is.Not.Null, "the loader returned a snapshot");

            using (var connection = OpenReadOnly(CanonicalDatabasePath))
            {
                Assert.AreEqual(CoreMigrations.MaxVersion, SchemaVersion(connection),
                    "the canonical database is at the current registered schema version");
            }

            // Per-item invariants only: the canonical store is legitimately
            // content-empty between authoring milestones, so asserting a count
            // would break the moment WPF authors a card. Whatever IS present
            // must be structurally complete.
            foreach (var session in content.Sessions)
            {
                Assert.Greater(session.Graph.Nodes.Count, 0, "session '" + session.Id + "' has a graph");
                Assert.That(session.CardWeighting, Is.Not.Null, "session '" + session.Id + "' has weighting");
            }

            foreach (var phase in content.Phases)
            {
                Assert.Greater(phase.Graph.Nodes.Count, 0, "phase '" + phase.Id + "' has a graph");
            }

            foreach (var card in content.Cards)
            {
                Assert.That(card.Sequence, Is.Not.Null, "card '" + card.Id + "' owns a sequence");
            }

            UnityEngine.Debug.Log(
                "[SqliteContentIntegration] canonical snapshot: schema v" + CoreMigrations.MaxVersion +
                ", sessions=" + content.Sessions.Count +
                ", phases=" + content.Phases.Count +
                ", cards=" + content.Cards.Count +
                ", sessionTypes=" + content.SessionTypes.Count);
        }

        [Test]
        public void Loader_RunsInsideACallerOwnedReadTransaction_AndLeavesNothingBehind()
        {
            // The loader issues many separate queries. A host that needs one
            // consistent view wraps them in its own transaction, so that has to
            // work against the vendored provider — it is not something a
            // successful isolated loader call would ever reveal.
            var workingPath = Path.Combine(Path.GetTempPath(),
                "truthcardgame-unity-readtxn-" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(CanonicalDatabasePath, workingPath);

            try
            {
                int sessionsInside;
                int cardsInside;

                using (var connection = OpenReadWrite(workingPath))
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        var content = GameContentSnapshotLoader.Load(connection, ensureSchema: false);
                        Assert.That(content, Is.Not.Null, "the loader ran inside a caller-owned transaction");
                        sessionsInside = content.Sessions.Count;
                        cardsInside = content.Cards.Count;
                        transaction.Rollback();
                    }

                    // The rolled-back read left the database exactly as it was:
                    // the loader read, and wrote nothing.
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA integrity_check;";
                        Assert.AreEqual("ok", command.ExecuteScalar() as string, "integrity after the read transaction");
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA foreign_key_check;";
                        using (var reader = command.ExecuteReader())
                        {
                            Assert.IsFalse(reader.Read(), "no foreign-key violations after the read transaction");
                        }
                    }

                    Assert.AreEqual(CoreMigrations.MaxVersion, SchemaVersion(connection),
                        "the read transaction changed no schema version");
                }

                using (var reopened = OpenReadOnly(workingPath))
                {
                    var content = GameContentSnapshotLoader.Load(reopened, ensureSchema: false);
                    Assert.AreEqual(sessionsInside, content.Sessions.Count, "session count is stable after rollback");
                    Assert.AreEqual(cardsInside, content.Cards.Count, "card count is stable after rollback");
                }

                Assert.DoesNotThrow(() => File.Delete(workingPath),
                    "the connection released the database file when it was disposed");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(workingPath)) File.Delete(workingPath);
            }
        }

        // ---------- real content: migrate, load, hand to the engine ----------

        [UnityTest]
        public IEnumerator MigratedLegacyDatabase_LoadsInUnity_AndDrivesGameSessionEngine()
        {
            Assert.That(File.Exists(LegacyFixturePath), Is.True,
                "legacy fixture not found at " + LegacyFixturePath);

            var workingPath = Path.Combine(Path.GetTempPath(),
                "truthcardgame-unity-fixture-" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(LegacyFixturePath, workingPath);

            try
            {
                GameContentDefinition content;

                // One connection: migrate the copy with the Unity-side schema
                // scripts, then read it with the shared loader.
                using (var connection = OpenReadWrite(workingPath))
                {
                    ConnectionInitializer.Initialize(connection);
                    var appliedVersion = CoreMigrator.EnsureSchema(connection);
                    Assert.AreEqual(CoreMigrations.MaxVersion, appliedVersion,
                        "the migrated copy reached the current schema version using StreamingAssets scripts");

                    content = GameContentSnapshotLoader.Load(connection, ensureSchema: false);

                    Assert.Greater(content.Sessions.Count, 0, "sessions survived the upgrade");
                    Assert.Greater(content.Phases.Count, 0, "phases survived the upgrade");
                    Assert.Greater(content.Cards.Count, 0, "cards survived the upgrade");

                    // The handoff the acceptance names: the loader's snapshot goes
                    // straight into the engine, with no adapter, mapping or copy.
                    var session = content.Sessions[0];
                    var log = new RecordingLog();
                    var services = new CoreServices(new ImmediateDelay(), log);
                    var engine = new GameSessionEngine(content, session.Id, services);

                    var cardsDrawn = new List<string>();
                    engine.CardStarted += card => cardsDrawn.Add(card.Id);

                    var guard = 0;
                    var waitingForContinue = false;
                    while (!engine.IsComplete && guard++ < 500)
                    {
                        var advance = waitingForContinue
                            ? engine.ContinueAsync(CancellationToken.None)
                            : engine.RunUntilYieldAsync(CancellationToken.None);

                        yield return Await(advance);

                        Assert.That(advance.IsCompletedSuccessfully, Is.True,
                            "session '" + session.Id + "' advance faulted: " +
                            (advance.Exception != null ? advance.Exception.GetBaseException().ToString() : "incomplete"));

                        var result = advance.Result;
                        // Unity ships an older NUnit than the DotNet suites
                        // (no Is.AnyOf), so this stays an explicit comparison.
                        var legalYield = result.Kind == AdvanceResultKind.WaitForContinue
                            || result.Kind == AdvanceResultKind.SessionCompleted;
                        Assert.That(legalYield, Is.True,
                            "session '" + session.Id + "' advanced cleanly (step " + guard +
                            ", kind " + result.Kind + ")");
                        waitingForContinue = result.Kind == AdvanceResultKind.WaitForContinue;
                    }

                    Assert.That(engine.IsComplete, Is.True,
                        "session '" + session.Id + "' completed through the Unity-compiled engine");
                    Assert.Greater(cardsDrawn.Count, 0, "the engine drew real cards from the loaded snapshot");
                    Assert.IsFalse(engine.IsBusy, "the engine went idle");
                    Assert.IsFalse(log.HasErrors, "no engine errors: " + string.Join(" | ", log.Entries));

                    UnityEngine.Debug.Log(
                        "[SqliteContentIntegration] migrated fixture run: session=" + session.Id +
                        ", cardsDrawn=" + cardsDrawn.Count + ", steps=" + guard);
                }

                // Connection release: the loader's contract is to let go of the
                // database before anything reads it again. A surviving handle
                // would break WPF-then-Unity reload at the next run boundary.
                Assert.DoesNotThrow(() => File.Delete(workingPath),
                    "the read released the database file when its connection was disposed");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(workingPath)) File.Delete(workingPath);
            }
        }

        // ---------- one CLR identity per portable type ----------

        [Test]
        public void ContentAndCoreTypes_ResolveToExactlyOneDefinition_AcrossLoadedAssemblies()
        {
            var definitions = new Dictionary<string, List<Assembly>>(StringComparer.Ordinal);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    var messages = new List<string>();
                    foreach (var loaderException in exception.LoaderExceptions)
                    {
                        messages.Add(loaderException == null ? "<null>" : loaderException.Message);
                    }
                    Assert.Fail("Reflection-load failure in assembly '" + AssemblyName(assembly) + "': " +
                        string.Join(" | ", messages));
                    throw;
                }

                foreach (var type in types)
                {
                    if (type == null || type.Namespace == null) continue;
                    if (!IsPortableNamespace(type.Namespace)) continue;

                    if (!definitions.TryGetValue(type.FullName, out var owners))
                    {
                        owners = new List<Assembly>();
                        definitions.Add(type.FullName, owners);
                    }
                    owners.Add(assembly);
                }
            }

            Assert.Greater(definitions.Count, 0, "portable type definitions were inspected");

            var duplicates = new List<string>();
            foreach (var pair in definitions)
            {
                if (pair.Value.Count > 1)
                {
                    var names = new List<string>();
                    foreach (var assembly in pair.Value) names.Add(AssemblyName(assembly));
                    duplicates.Add(pair.Key + " in " + string.Join(", ", names));
                }
            }

            Assert.IsEmpty(duplicates,
                "competing definitions of portable types found — a compiled Game.Content/Game.Core DLL has been " +
                "imported alongside Unity's portable sources: " + string.Join(" | ", duplicates));

            // Exactly one assembly supplies each portable assembly's types.
            AssertSingleAssembly("Game.Content");
            AssertSingleAssembly("Game.Core");
            AssertSingleAssembly("Game.Content.Sqlite");

            Assert.AreEqual("Game.Content", typeof(GameContentDefinition).Assembly.GetName().Name);
            Assert.AreEqual("Game.Core", typeof(GameSessionEngine).Assembly.GetName().Name);
            Assert.AreEqual("Game.Content.Sqlite", typeof(GameContentSnapshotLoader).Assembly.GetName().Name);

            // The namespaces scanned above must actually cover what we care about.
            Assert.That(definitions.ContainsKey(typeof(GameContentDefinition).FullName), Is.True,
                "the duplicate scan covered " + typeof(GameContentDefinition).FullName);
            Assert.That(definitions.ContainsKey(typeof(GameSessionEngine).FullName), Is.True,
                "the duplicate scan covered " + typeof(GameSessionEngine).FullName);
        }

        [Test]
        public void LoaderReturnType_AndEngineParameterType_AreTheUnityGameContentDefinition()
        {
            var load = typeof(GameContentSnapshotLoader).GetMethod(
                "Load", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(DbConnection), typeof(bool) }, null);

            Assert.That(load, Is.Not.Null, "GameContentSnapshotLoader.Load(DbConnection, bool) exists in Unity");
            Assert.That(load.ReturnType, Is.EqualTo(typeof(GameContentDefinition)),
                "the loader's declared return type is exactly Unity's typeof(GameContentDefinition)");

            var constructor = typeof(GameSessionEngine).GetConstructor(
                new[] { typeof(GameContentDefinition), typeof(string), typeof(CoreServices) });

            Assert.That(constructor, Is.Not.Null,
                "GameSessionEngine(GameContentDefinition, string, CoreServices) exists — " +
                "this lookup itself fails if the two Types differ");

            var parameters = constructor.GetParameters();
            Assert.That(parameters[0].ParameterType, Is.EqualTo(load.ReturnType),
                "the engine's content parameter is the exact Type the loader returns");
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(GameContentDefinition)));

            // Every engine entry point takes that one content type.
            foreach (var candidate in typeof(GameSessionEngine).GetConstructors())
            {
                Assert.That(candidate.GetParameters()[0].ParameterType, Is.EqualTo(typeof(GameContentDefinition)),
                    "constructor " + candidate + " takes exactly Unity's GameContentDefinition");
            }
        }

        // ---------- the schema-script path ----------

        [Test]
        public void SchemaScripts_ReadFromStreamingAssets_InUnity()
        {
            Assert.That(Directory.Exists(SchemaScriptDirectory), Is.True,
                "schema script folder missing at " + SchemaScriptDirectory);

            Assert.That(CoreMigrations.All.Count, Is.GreaterThan(0), "at least one registered migration");

            // Derived rather than hardcoded: this test is about the script path,
            // so registering a migration must not require editing it. Versions
            // are contiguous from 1, which makes the count and the highest
            // version agree while still catching a gap or a duplicate.
            Assert.AreEqual(CoreMigrations.MaxVersion, CoreMigrations.All.Count,
                "registered migration versions are contiguous from 1");

            var previousVersion = 0;
            foreach (var migration in CoreMigrations.All)
            {
                Assert.Greater(migration.Version, previousVersion,
                    "migration versions increase in registration order");
                previousVersion = migration.Version;
            }

            foreach (var migration in CoreMigrations.All)
            {
                var script = SchemaScripts.Load(migration.ScriptFileName);
                Assert.That(script, Is.Not.Null.And.Not.Empty,
                    "migration " + migration.Version + " (" + migration.Name + ") resolved a script");
                Assert.That(script, Does.Contain(";"), migration.ScriptFileName + " looks like SQL");
            }

            Assert.AreEqual(CoreMigrations.All.Count, Directory.GetFiles(SchemaScriptDirectory, "*.sql").Length,
                "StreamingAssets holds exactly the registered scripts");
        }

        [Test]
        public void MissingReader_FailsLoudly_AndVersionInspectionNeverNeedsAScript()
        {
            UnitySchemaScripts.Uninstall();

            var exception = Assert.Throws<InvalidOperationException>(
                () => SchemaScripts.Load("SQLITE-SCHEMA-V1.sql"));
            Assert.That(exception.Message, Does.Contain("SQLITE-SCHEMA-V1.sql"),
                "the failure names the script it could not read");
            Assert.That(exception.Message, Does.Contain("Reader"),
                "the failure names the remedy rather than returning an empty script");

            // Declaring a migration must not read or embed anything; only
            // resolving its text does. That is what lets a read-only host check
            // the stored schema version without any scripts available.
            var probe = new CoreMigration(99, "probe", "SQLITE-SCHEMA-DOES-NOT-EXIST.sql");
            Assert.AreEqual("SQLITE-SCHEMA-DOES-NOT-EXIST.sql", probe.ScriptFileName);
            Assert.Throws<InvalidOperationException>(() =>
            {
                var unused = probe.Script;
            });
            // Version inspection must not depend on script text, so with the
            // scripts uninstalled the registered maximum is still readable — and
            // registering a migration must not require editing this line.
            Assert.Greater(CoreMigrations.MaxVersion, 0,
                "version inspection never needs a script");
        }

        // ---------- helpers ----------

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }

        private static SqliteConnection OpenReadOnly(string path)
        {
            var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;Pooling=False");
            connection.Open();
            return connection;
        }

        private static SqliteConnection OpenReadWrite(string path)
        {
            var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False");
            connection.Open();
            return connection;
        }

        private static int SchemaVersion(DbConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MAX(version) FROM core_schema_migration;";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private static bool IsPortableNamespace(string typeNamespace)
        {
            return typeNamespace == "TruthCardGame.Content"
                || typeNamespace.StartsWith("TruthCardGame.Content.", StringComparison.Ordinal)
                || typeNamespace == "TruthCardGame.Core"
                || typeNamespace.StartsWith("TruthCardGame.Core.", StringComparison.Ordinal);
        }

        private static void AssertSingleAssembly(string assemblyName)
        {
            var matches = new List<Assembly>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (AssemblyName(assembly) == assemblyName) matches.Add(assembly);
            }

            Assert.AreEqual(1, matches.Count,
                "expected exactly one loaded assembly named '" + assemblyName + "'");

            var location = matches[0].Location;
            Assert.That(string.IsNullOrEmpty(location) || !location.Replace('\\', '/').Contains("/Plugins/"),
                Is.True,
                "assembly '" + assemblyName + "' must be compiled from Assets/Scripts/Portable, not imported from a plugin: " + location);
        }

        private static string AssemblyName(Assembly assembly)
        {
            var name = assembly.GetName();
            return name == null ? "<unnamed>" : name.Name;
        }
    }
}
