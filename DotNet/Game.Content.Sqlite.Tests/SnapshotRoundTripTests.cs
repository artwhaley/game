using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Content.Sqlite.Tests
{
    [TestFixture]
    public class SnapshotRoundTripTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "sqlite-roundtrip-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            ConnectionInitializer.Initialize(_connection);
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private static GameContentDefinition Sample() => SampleContent.Build();

        [Test]
        public void InitializeThenLoad_ProducesEquivalentSnapshot()
        {
            var source = Sample();
            DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, source);
            var loaded = GameContentSnapshotLoader.Load(_connection);

            Assert.AreEqual(2, loaded.Sessions.Count);
            Assert.AreEqual(6, loaded.Phases.Count);
            Assert.AreEqual(7, loaded.Cards.Count);
            Assert.AreEqual(5, loaded.Actions.Count);
            Assert.AreEqual(1, loaded.Resources.Count);
            Assert.AreEqual("cs:intro", loaded.Resources[0].Id);
            Assert.AreEqual("cutscene", loaded.Resources[0].Kind);

            // Deck order preserved (same ids as StarterDeck).
            CollectionAssert.AreEqual(
                new[]
                {
                    "cb5e05de6a3b437c950f25fbd42c1ff3",
                    "b6839ac247c642ab86a78e86a68a1bee",
                    "29e2b4399a0d4d57861cd481cb42c683",
                    "3f7191b1557a4907bb53fa8ff1788aa3",
                    "d00dcc10e12c4e71a131b45ef3df0e19",
                    "54e39eb4014f4b73b017a9315b130c21",
                    "5fea312b53554d3f80428480c3c2cf3b"
                },
                loaded.Deck.CardIds.ToArray());

            // Session slot/candidate structure with transitional ids.
            var intense = loaded.Sessions.Single(s => s.Title == "Intense");
            Assert.AreEqual(3, intense.PhaseSlots.Count);
            Assert.AreEqual("legacy-slot:9ac9fbe9ced7463a8627356e21644096:8965075142604a72a36593c6ed437788:0", intense.PhaseSlots[0].Id);
            Assert.AreEqual(1, intense.PhaseSlots[0].Candidates.Count);
            Assert.AreEqual("8965075142604a72a36593c6ed437788", intense.PhaseSlots[0].Candidates[0].PhaseId);

            // Phase tags preserved as strings in ordinal order.
            var build = loaded.Phases.Single(p => p.Id == "8965075142604a72a36593c6ed437788");
            CollectionAssert.AreEqual(new[] { "party" }, build.MustIncludeTags.ToArray());

            // Card tag + action order preserved.
            var twin = loaded.Cards.Single(c => c.Title == "Twin Whispers");
            CollectionAssert.AreEqual(new[] { "solo" }, twin.Tags.ToArray());
            CollectionAssert.AreEqual(
                new[] { "3d0a65f2c63147568656c4f95e8ff774", "3d0a65f2c63147568656c4f95e8ff774" },
                twin.ActionIds.ToArray());

            // Choice option with a child Action id and a null child.
            var choice = loaded.Actions.OfType<ChoiceActionDefinition>().Single();
            Assert.AreEqual(2, choice.Options.Count);
            Assert.AreEqual("Own it", choice.Options[0].Label);
            Assert.AreEqual("d0b7a66bba314acb94c6133d22d6a732", choice.Options[0].ChildActionId);
            Assert.AreEqual("Shrug it off", choice.Options[1].Label);
            Assert.AreEqual("3d0a65f2c63147568656c4f95e8ff774", choice.Options[1].ChildActionId);

            // Cutscene resource id preserved.
            var cutscene = loaded.Actions.OfType<CutsceneActionDefinition>().Single();
            Assert.AreEqual("cs:intro", cutscene.ResourceId);
            Assert.IsTrue(cutscene.IsBlocking);

            // Debug action delay preserved (schema addition).
            var debug = loaded.Actions.OfType<DebugActionDefinition>().Single(a => a.Id == "3d0a65f2c63147568656c4f95e8ff774");
            Assert.AreEqual(5f, debug.DelaySeconds);
            Assert.IsFalse(debug.IsBlocking);
        }

        [Test]
        public void SharedAction_LoadedOnce_ReferencedManyTimes()
        {
            var source = Sample();
            DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, source);
            var loaded = GameContentSnapshotLoader.Load(_connection);

            // The continuous debug action is referenced twice on Twin Whispers
            // and once as the choice's second child — yet it exists exactly once.
            var continuous = loaded.Actions.OfType<DebugActionDefinition>().Single(a => a.Id == "3d0a65f2c63147568656c4f95e8ff774");
            var twin = loaded.Cards.Single(c => c.Title == "Twin Whispers");
            Assert.AreEqual(2, twin.ActionIds.Count(id => id == continuous.Id));
            Assert.AreEqual(1, loaded.Actions.Count(a => a.Id == continuous.Id));
        }

        [Test]
        public void Initialize_RefusesNonEmptyDatabase()
        {
            DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, Sample());

            var ex = Assert.Throws<InvalidOperationException>(
                () => DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, Sample()));
            StringAssert.Contains("non-empty", ex.Message);
        }

        [Test]
        public void Initialize_LeavesHostExtensionTablesUntouched()
        {
            Execute(_connection, "CREATE TABLE unity_fake_extension (id TEXT PRIMARY KEY, payload TEXT NOT NULL);");
            Execute(_connection, "INSERT INTO unity_fake_extension (id, payload) VALUES ('b1', 'binding');");

            DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, Sample());

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT payload FROM unity_fake_extension WHERE id = 'b1';";
                Assert.AreEqual("binding", command.ExecuteScalar());
            }
        }

        [Test]
        public void Load_FailsOnUnknownActionType()
        {
            DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, Sample());
            Execute(_connection,
                "INSERT INTO action (id, action_type, is_blocking) VALUES ('a-unknown', 'teleport', 1);");

            var ex = Assert.Throws<InvalidOperationException>(() => GameContentSnapshotLoader.Load(_connection));
            StringAssert.Contains("unknown action_type 'teleport'", ex.Message);
        }

        [Test]
        public void Load_FailsOnMissingSubtypeRow()
        {
            DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, Sample());
            Execute(_connection,
                "INSERT INTO action (id, action_type, is_blocking) VALUES ('a-choice', 'choice', 1);");

            var ex = Assert.Throws<InvalidOperationException>(() => GameContentSnapshotLoader.Load(_connection));
            StringAssert.Contains("no action_choice row", ex.Message);
        }

        [Test]
        public void Load_FailsOnContradictorySubtypeState()
        {
            DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, Sample());
            // Declares debug but carries a stat_increase subtype row.
            Execute(_connection,
                "INSERT INTO action (id, action_type, is_blocking) VALUES ('a-mixed', 'debug', 1);");
            Execute(_connection,
                "INSERT INTO action_stat_increase (action_id, stat_key, amount) VALUES ('a-mixed', 'courage', 1);");

            var ex = Assert.Throws<InvalidOperationException>(() => GameContentSnapshotLoader.Load(_connection));
            StringAssert.Contains("contradictory subtype state", ex.Message);
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
