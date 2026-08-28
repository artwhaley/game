using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Constraint proofs for the v5 schema: real FK constraints, owned
    /// children cascade, reusable references restrict, unique (parent,
    /// ordinal), and repeatable references. The v1-era slot/deck/action
    /// structures these tests originally covered were dropped by migration 5
    /// (Docs/MilestoneB/02-schema-audit.md); the same contract classes are
    /// proven against their v5 replacements.
    /// </summary>
    [TestFixture]
    public class SchemaConstraintTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "sqlite-schema-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        // --- Rejection tests -------------------------------------------------

        [Test]
        public void Rejects_CardTagWithMissingCard()
        {
            InsertCardTagDefinition("tag-a", "A");
            Assert.Throws<SqliteException>(() => InsertCardTag("no-such-card", "tag-a", 0));
        }

        [Test]
        public void Rejects_CardTagWithMissingDefinition()
        {
            InsertCard("card1", "C");
            Assert.Throws<SqliteException>(() => InsertCardTag("card1", "no-such-tag", 0));
        }

        [Test]
        public void Rejects_CardKinkWithMissingKinkDefinition()
        {
            InsertCard("card1", "C");
            Assert.Throws<SqliteException>(() =>
                Execute("INSERT INTO card_kink (card_id, kink_id, ordinal) VALUES ('card1','no-such-kink',0);"));
        }

        [Test]
        public void Rejects_CardRequiredEquipmentWithMissingDefinition()
        {
            InsertCard("card1", "C");
            Assert.Throws<SqliteException>(() =>
                Execute("INSERT INTO card_required_equipment (card_id, equipment_id, ordinal) VALUES ('card1','no-such-eq',0);"));
        }

        [Test]
        public void Rejects_CardRequiredCapabilityWithMissingDefinition()
        {
            InsertCard("card1", "C");
            Assert.Throws<SqliteException>(() =>
                Execute("INSERT INTO card_required_smart_toy_capability (card_id, capability_id, ordinal) VALUES ('card1','no-such-cap',0);"));
        }

        [Test]
        public void Rejects_PhaseCardQueryTagWithMissingDefinition()
        {
            InsertPhase("phase1", "P");
            Assert.Throws<SqliteException>(() =>
                Execute("INSERT INTO phase_card_all_tag (phase_id, tag_id, ordinal) VALUES ('phase1','no-such-tag',0);"));
        }

        [Test]
        public void Rejects_SessionTypeCapabilityWithMissingDefinition()
        {
            Execute("INSERT INTO session_type (id, title) VALUES ('type-x','X');");
            Assert.Throws<SqliteException>(() =>
                Execute("INSERT INTO session_type_required_smart_toy_capability (session_type_id, capability_id, ordinal) VALUES ('type-x','no-such-cap',0);"));
        }

        [Test]
        public void Rejects_DuplicateParentOrdinal_CardTags()
        {
            InsertCardTagDefinition("tag-a", "A");
            InsertCardTagDefinition("tag-b", "B");
            InsertCard("card1", "C");
            InsertCardTag("card1", "tag-a", 0);
            Assert.Throws<SqliteException>(() => InsertCardTag("card1", "tag-b", 0));
        }

        [Test]
        public void Rejects_DuplicateCardTagPair()
        {
            InsertCardTagDefinition("tag-a", "A");
            InsertCard("card1", "C");
            InsertCardTag("card1", "tag-a", 0);
            Assert.Throws<SqliteException>(() => InsertCardTag("card1", "tag-a", 1));
        }

        [Test]
        public void Rejects_DeleteOfReferencedCardTagDefinition()
        {
            InsertCardTagDefinition("tag-a", "A");
            InsertCard("card1", "C");
            InsertCardTag("card1", "tag-a", 0);

            Assert.Throws<SqliteException>(() => Delete("card_tag_definition", "tag-a"));
        }

        [Test]
        public void Rejects_NegativeSessionWeighting()
        {
            InsertSession("s1", "S", "type-standard");
            Assert.Throws<SqliteException>(() =>
                Execute("INSERT INTO session_card_weighting (session_id, love_base) VALUES ('s1', -1);"));
        }

        // --- Cascade tests ----------------------------------------------------

        [Test]
        public void Cascades_CardDeletesRelations_ButNotDefinitions()
        {
            InsertCardTagDefinition("tag-a", "A");
            InsertKink("kink-1", "K");
            InsertEquipment("eq-1", "E");
            InsertCapability("cap-1", "C");
            InsertCard("card1", "Card");
            InsertCardTag("card1", "tag-a", 0);
            Execute("INSERT INTO card_kink (card_id, kink_id, ordinal) VALUES ('card1','kink-1',0);");
            Execute("INSERT INTO card_required_equipment (card_id, equipment_id, ordinal) VALUES ('card1','eq-1',0);");
            Execute("INSERT INTO card_required_smart_toy_capability (card_id, capability_id, ordinal) VALUES ('card1','cap-1',0);");

            Delete("card", "card1");

            AssertRowCount("card_tag", 0);
            AssertRowCount("card_kink", 0);
            AssertRowCount("card_required_equipment", 0);
            AssertRowCount("card_required_smart_toy_capability", 0);
            AssertRowCount("card_tag_definition", 1);
            AssertRowCount("kink_definition", 1);
            AssertRowCount("equipment_definition", 1);
            AssertRowCount("smart_toy_capability_definition", 1);
        }

        [Test]
        public void Cascades_PhaseDeletesQueryTags_ButNotDefinitions()
        {
            InsertCardTagDefinition("tag-a", "A");
            InsertPhase("phase1", "P");
            Execute("INSERT INTO phase_card_all_tag (phase_id, tag_id, ordinal) VALUES ('phase1','tag-a',0);");
            Execute("INSERT INTO phase_card_any_tag (phase_id, tag_id, ordinal) VALUES ('phase1','tag-a',0);");

            Delete("phase", "phase1");

            AssertRowCount("phase_card_all_tag", 0);
            AssertRowCount("phase_card_any_tag", 0);
            AssertRowCount("card_tag_definition", 1);
        }

        [Test]
        public void Cascades_SessionDeletesWeighting()
        {
            InsertSession("s1", "S", "type-standard");
            Execute("INSERT INTO session_card_weighting (session_id) VALUES ('s1');");
            AssertRowCount("session_card_weighting", 1);
            Delete("session", "s1");
            AssertRowCount("session_card_weighting", 0);
        }

        [Test]
        public void Cascades_SessionTypeDeletesCapabilityRequirements()
        {
            InsertCapability("cap-1", "C");
            Execute("INSERT INTO session_type (id, title) VALUES ('type-x','X');");
            Execute("INSERT INTO session_type_required_smart_toy_capability (session_type_id, capability_id, ordinal) VALUES ('type-x','cap-1',0);");

            Execute("DELETE FROM session_type WHERE id='type-x';");

            AssertRowCount("session_type_required_smart_toy_capability", 0);
            AssertRowCount("smart_toy_capability_definition", 1);
        }

        // --- Repeatable references (positive) ----------------------------------

        [Test]
        public void Allows_SameCardTagDefinition_AcrossManyCards()
        {
            InsertCardTagDefinition("tag-a", "A");
            InsertCard("card1", "C1");
            InsertCard("card2", "C2");
            InsertCardTag("card1", "tag-a", 0);
            InsertCardTag("card2", "tag-a", 0);

            AssertRowCount("card_tag", 2);
            AssertRowCount("card_tag_definition", 1);
        }

        [Test]
        public void Allows_SameTag_InPhaseAllAndAnyQueries()
        {
            InsertCardTagDefinition("tag-a", "A");
            InsertPhase("phase1", "P");
            Execute("INSERT INTO phase_card_all_tag (phase_id, tag_id, ordinal) VALUES ('phase1','tag-a',0);");
            Execute("INSERT INTO phase_card_any_tag (phase_id, tag_id, ordinal) VALUES ('phase1','tag-a',0);");

            AssertRowCount("phase_card_all_tag", 1);
            AssertRowCount("phase_card_any_tag", 1);
        }

        // --- Helpers -----------------------------------------------------------

        private void InsertSession(string id, string title, string typeId) =>
            Execute($"INSERT INTO session (id, title, session_type_id) VALUES ('{id}', '{title}', '{typeId}');");
        private void InsertPhase(string id, string title) => Execute($"INSERT INTO phase (id, title, min_cards, max_cards) VALUES ('{id}', '{title}', 0, 0);");
        private void InsertCard(string id, string title) => Execute($"INSERT INTO card (id, title) VALUES ('{id}', '{title}');");

        private void InsertCardTagDefinition(string id, string title) =>
            Execute($"INSERT INTO card_tag_definition (id, title) VALUES ('{id}', '{title}');");
        private void InsertKink(string id, string title) =>
            Execute($"INSERT INTO kink_definition (id, title) VALUES ('{id}', '{title}');");
        private void InsertEquipment(string id, string title) =>
            Execute($"INSERT INTO equipment_definition (id, title) VALUES ('{id}', '{title}');");
        private void InsertCapability(string id, string title) =>
            Execute($"INSERT INTO smart_toy_capability_definition (id, title) VALUES ('{id}', '{title}');");

        private void InsertCardTag(string cardId, string tagId, int ordinal) =>
            Execute($"INSERT INTO card_tag (card_id, tag_id, ordinal) VALUES ('{cardId}', '{tagId}', {ordinal});");

        private void Delete(string table, string id) => Execute($"DELETE FROM {table} WHERE id = '{id}';");

        private void AssertRowCount(string table, long expected)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = $"SELECT COUNT(*) FROM {table};";
                Assert.AreEqual(expected, Convert.ToInt64(command.ExecuteScalar()));
            }
        }

        private void Execute(string sql)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
