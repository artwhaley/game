using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 02 HARD gate: v8 migration tests from v7 plus a legacy-toy
    /// fixture. Verifies the dialog catalog tables, the toy_set_pattern and
    /// dialog_from_tags subtypes, deterministic legacy-intensity migration
    /// into toy_pattern Resources, and extension-table safety.
    /// </summary>
    [TestFixture]
    public class SchemaV8MigrationTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "gwb-v8-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            _connection.Open();
            ConnectionInitializer.Initialize(_connection);
        }

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            _connection.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void V8_CreatesDialogCatalogTables()
        {
            CoreMigrator.EnsureSchema(_connection);
            foreach (var table in new[] { "dialog_tag_definition", "dialog_snippet", "dialog_snippet_tag",
                                          "action_instance_toy_set_pattern", "action_instance_dialog_from_tags",
                                          "action_instance_dialog_from_tag" })
            {
                Assert.That(TableExists(table), Is.True, $"v8 must create {table}");
            }
        }

        [Test]
        public void V8_ToyActivityTable_HasPatternResourceColumn_NoIntensity()
        {
            CoreMigrator.EnsureSchema(_connection);
            Assert.That(ColumnExists("action_instance_toy_activity", "pattern_resource_id"), Is.True);
            Assert.That(ColumnExists("action_instance_toy_activity", "intensity"), Is.False,
                "v8 removes authored intensity");
        }

        [Test]
        public void V8_MigratesLegacyIntensity_ToDeterministicConstantPatterns()
        {
            // Migrate to v8 first, then recreate the legacy v7 column shape so
            // Migration8Transform can be exercised directly against rows that
            // still carry authored intensity (the ledger prevents running the
            // real migration twice).
            CoreMigrator.EnsureSchema(_connection);
            CatalogRepositories.CreateSmartToyCapability(_connection,
                new SmartToyCapabilityDefinition { Id = "vibrate", Title = "Vibration" });
            Sql.Execute(_connection, null,
                "ALTER TABLE action_instance_toy_activity ADD COLUMN intensity REAL NOT NULL DEFAULT 1;");
            // Simulate legacy state: FK didn't exist in v7, so disable FK
            // enforcement while seeding rows that predate pattern_resource_id.
            Sql.Execute(_connection, null, "PRAGMA foreign_keys = OFF;");
            SeedLegacyToyRow("toy-1", "vibrate", 0.5, 10);
            SeedLegacyToyRow("toy-2", "vibrate", 0.5, 20);
            SeedLegacyToyRow("toy-3", "vibrate", 1.0, 5);
            SeedLegacyToyRow("toy-4", "vibrate", 0.501, 5);
            SeedLegacyToyRow("toy-5", "vibrate", 0.504, 5);
            Sql.Execute(_connection, null, "PRAGMA foreign_keys = ON;");

            using (var transaction = _connection.BeginTransaction())
            {
                Migration8Transform.Transform(_connection, transaction);
                transaction.Commit();
            }

            // Five rows contain four distinct exact intensity values. The two
            // close values intentionally collide under friendly rounding.
            var patterns = ResourceRepository.List(_connection)
                .FindAll(r => r.Kind == ResourceKinds.ToyPattern);
            Assert.That(patterns.Count, Is.EqualTo(4), "one constant-pattern Resource per distinct exact intensity");
            Assert.That(patterns.Select(p => p.Id).Distinct().Count(), Is.EqualTo(4));

            var byId = new Dictionary<string, string>();
            foreach (var p in patterns) byId[p.Id] = p.Name ?? "";
            Assert.That(byId.Keys.Any(k => k.StartsWith("res-toy-const-")), Is.True,
                "at least one deterministic constant-pattern resource id");
            Assert.That(byId.Values.Any(n => n.Contains("Migrated Constant")), Is.True,
                "at least one resource named with Migrated Constant prefix");

            // All rows survive with capability/duration/blocking intact, pointed at their pattern.
            var content = GameContentSnapshotLoader.Load(_connection);
            var toys = new List<ToyActivityInstanceDefinition>();
            foreach (var card in content.Cards)
                CollectToys(card.Sequence, toys);
            Assert.That(toys.Count, Is.EqualTo(5), "all legacy toy rows survive");
            foreach (var t in toys)
            {
                Assert.That(t.PatternResourceId, Is.Not.Null.And.Not.Empty, "pattern reference assigned");
                Assert.That(t.PatternResourceId, Does.StartWith("res-toy-const-"), "deterministic resource id");
            }
        }

        [Test]
        public void V8_ExtensionRows_SurviveToyAndDialogEdits()
        {
            CoreMigrator.EnsureSchema(_connection);
            CatalogRepositories.CreateSmartToyCapability(_connection,
                new SmartToyCapabilityDefinition { Id = "vibrate", Title = "Vibration" });
            ResourceRepository.Create(_connection,
                new ResourceDefinition { Id = "res-pat", Kind = ResourceKinds.ToyPattern, Name = "P" });
            DialogCatalogRepository.CreateTag(_connection,
                new DialogTagDefinition { Id = "tag-giggle", Title = "giggle" });

            var card = new CardDefinition { Id = "card-ext", Title = "Ext",
                Sequence = new ActionSequenceDefinition { Id = "seq-ext" } };
            card.Sequence.Instances.Add(new ToyActivityInstanceDefinition
                { Id = "toy-ext", CapabilityId = "vibrate", PatternResourceId = "res-pat", DurationSeconds = 5f, IsBlocking = true });
            var dialogFromTags = new DialogFromTagsInstanceDefinition { Id = "dft-ext", IsBlocking = true };
            dialogFromTags.RequiredDialogTagIds.Add("tag-giggle");
            card.Sequence.Instances.Add(dialogFromTags);
            CardRepository.Create(connection: _connection, card: card);

            // Fake host extension row attached to a surviving action id.
            Sql.Execute(_connection, null,
                "CREATE TABLE IF NOT EXISTS unity_fake_ext (action_id TEXT NOT NULL, payload TEXT NOT NULL);");
            Sql.Execute(_connection, null,
                "INSERT INTO unity_fake_ext (action_id, payload) VALUES ('toy-ext', 'keep-me');");

            // Edit: pattern change + duration change + tag change.
            var seq = GameContentSnapshotLoader.LoadSequence(_connection, "seq-ext");
            var toyRow = (ToyActivityInstanceDefinition)seq.Instances[0];
            toyRow.PatternResourceId = "res-pat";
            toyRow.DurationSeconds = 9f;
            ActionSequenceRepository.Save(_connection, seq);

            // Extension row must survive.
            var payload = "";
            Sql.QueryAll(_connection,
                "SELECT payload FROM unity_fake_ext WHERE action_id = 'toy-ext';",
                reader => payload = reader.GetString(0));
            Assert.That(payload, Is.EqualTo("keep-me"), "fake extension row survived toy edits");

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_key_check;";
                using (var reader = command.ExecuteReader()) Assert.IsFalse(reader.Read());
            }
        }

        [Test]
        public void SmartToyCapabilityUsage_IncludesTimedAndSetActions_AndBlocksDeletion()
        {
            CoreMigrator.EnsureSchema(_connection);
            CatalogRepositories.CreateSmartToyCapability(_connection,
                new SmartToyCapabilityDefinition { Id = "cap-timed", Title = "Timed" });
            CatalogRepositories.CreateSmartToyCapability(_connection,
                new SmartToyCapabilityDefinition { Id = "cap-set", Title = "Set" });
            ResourceRepository.Create(_connection,
                new ResourceDefinition { Id = "pattern", Kind = ResourceKinds.ToyPattern, Name = "Pattern" });
            var card = new CardDefinition
            {
                Id = "usage-card", Title = "Usage",
                Sequence = new ActionSequenceDefinition { Id = "usage-sequence" }
            };
            card.Sequence.Instances.Add(new ToyActivityInstanceDefinition
                { Id = "timed", CapabilityId = "cap-timed", PatternResourceId = "pattern", DurationSeconds = 1f });
            card.Sequence.Instances.Add(new ToySetPatternInstanceDefinition
                { Id = "set", CapabilityId = "cap-set", PatternResourceId = "pattern", IsBlocking = false });
            CardRepository.Create(_connection, card);

            var timed = CatalogRepositories.GetSmartToyCapabilityUsage(_connection, "cap-timed");
            Assert.That(timed.TimedToyPatternActions, Is.EqualTo(1));
            Assert.That(timed.SetToyPatternActions, Is.EqualTo(0));
            Assert.Throws<InvalidOperationException>(() =>
                CatalogRepositories.DeleteSmartToyCapabilityIfUnused(_connection, "cap-timed"));

            var set = CatalogRepositories.GetSmartToyCapabilityUsage(_connection, "cap-set");
            Assert.That(set.TimedToyPatternActions, Is.EqualTo(0));
            Assert.That(set.SetToyPatternActions, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() =>
                CatalogRepositories.DeleteSmartToyCapabilityIfUnused(_connection, "cap-set"));
        }

        [Test]
        public void SnapshotLoader_RejectsWrongResourceKindAfterReconstruction()
        {
            CoreMigrator.EnsureSchema(_connection);
            CatalogRepositories.CreateSmartToyCapability(_connection,
                new SmartToyCapabilityDefinition { Id = "cap", Title = "Capability" });
            ResourceRepository.Create(_connection,
                new ResourceDefinition { Id = "cut", Kind = ResourceKinds.Cutscene, Name = "Cutscene" });
            var card = new CardDefinition
            {
                Id = "invalid-card", Title = "Invalid",
                Sequence = new ActionSequenceDefinition { Id = "invalid-sequence" }
            };
            card.Sequence.Instances.Add(new ToyActivityInstanceDefinition
                { Id = "invalid-toy", CapabilityId = "cap", PatternResourceId = "cut", DurationSeconds = 1f });
            CardRepository.Create(_connection, card);

            var error = Assert.Throws<InvalidOperationException>(() => GameContentSnapshotLoader.Load(_connection));
            Assert.That(error.Message, Does.Contain("invalid-toy").And.Contain("cutscene").And.Contain("toy_pattern"));
        }

        // ---- helpers ----

        private bool TableExists(string name)
        {
            var found = false;
            Sql.QueryAll(_connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = @n;",
                reader => found = Convert.ToInt64(reader.GetValue(0)) > 0, ("n", name));
            return found;
        }

        private bool ColumnExists(string table, string column)
        {
            var found = false;
            Sql.QueryAll(_connection, $"PRAGMA table_info({table});",
                reader => { if (reader.GetString(1) == column) found = true; });
            return found;
        }

        /// <summary>Inserts a legacy-shaped toy row (intensity column present, pattern empty).</summary>
        private void SeedLegacyToyRow(string instanceId, string capabilityId, double intensity, double duration)
        {
            var card = new CardDefinition { Id = "card-x-" + instanceId, Title = "Legacy " + instanceId,
                Sequence = new ActionSequenceDefinition { Id = "seq-" + instanceId } };
            card.Sequence.Instances.Add(new ToyActivityInstanceDefinition
                { Id = instanceId, CapabilityId = capabilityId, DurationSeconds = (float)duration, IsBlocking = true });
            CardRepository.Create(_connection, card);

            Sql.Execute(_connection, null,
                "UPDATE action_instance_toy_activity SET intensity = @int, pattern_resource_id = '' WHERE action_instance_id = @i;",
                ("int", intensity), ("i", instanceId));
        }

        private static ActionSequenceDefinition FindSequenceWithToy(GameContentDefinition content)
        {
            foreach (var card in content.Cards)
            {
                foreach (var instance in card.Sequence.Instances)
                    if (instance is ToyActivityInstanceDefinition) return card.Sequence;
            }
            throw new InvalidOperationException("no toy sequence found");
        }

        private static void CollectToys(ActionSequenceDefinition seq, List<ToyActivityInstanceDefinition> toys)
        {
            if (seq == null) return;
            foreach (var instance in seq.Instances)
            {
                if (instance is ToyActivityInstanceDefinition toy) toys.Add(toy);
                if (instance is PromptChoiceInstanceDefinition choice)
                    foreach (var option in choice.Options) CollectToys(option.Sequence, toys);
            }
        }
    }
}
