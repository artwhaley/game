using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 04: prove the v1 schema actually enforces the contract —
    /// real FK constraints, owned children cascade, reusable references
    /// restrict, unique (parent, ordinal), and repeatable references.
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
        public void Rejects_CandidateWithMissingPhase()
        {
            InsertSession("s1", "S");
            InsertSlot("s1", "slot1", 0, "Warmup");
            Assert.Throws<SqliteException>(() => InsertCandidate("slot1", "c1", 0, "no-such-phase"));
        }

        [Test]
        public void Rejects_CardActionWithMissingAction()
        {
            InsertCard("card1", "C");
            Assert.Throws<SqliteException>(() => InsertCardAction("card1", 0, "no-such-action"));
        }

        [Test]
        public void Rejects_ChoiceChildWithMissingAction()
        {
            InsertAction("choice1", "choice", true);
            InsertChoice("choice1", "Pick");
            Assert.Throws<SqliteException>(() => InsertChoiceOption("opt1", "choice1", 0, "Go", "no-such-action"));
        }

        [Test]
        public void Rejects_MissingTag()
        {
            InsertCard("card1", "C");
            Assert.Throws<SqliteException>(() => InsertCardTag("card1", "no-such-tag", 0));
        }

        [Test]
        public void Rejects_DuplicateParentOrdinal()
        {
            InsertSession("s1", "S");
            InsertSlot("s1", "slot1", 0, "A");
            Assert.Throws<SqliteException>(() => InsertSlot("s1", "slot2", 0, "B"));
        }

        [Test]
        public void Rejects_DeleteOfReferencedReusablePhase()
        {
            InsertPhase("phase1", "P", 1, 5);
            InsertSession("s1", "S");
            InsertSlot("s1", "slot1", 0, "Warmup");
            InsertCandidate("slot1", "c1", 0, "phase1");

            Assert.Throws<SqliteException>(() => Delete("phase", "phase1"));
        }

        [Test]
        public void Rejects_DeleteOfReferencedReusableAction()
        {
            InsertAction("a1", "debug", false);
            InsertCard("card1", "C");
            InsertCardAction("card1", 0, "a1");

            Assert.Throws<SqliteException>(() => Delete("action", "a1"));
        }

        // --- Cascade tests ----------------------------------------------------

        [Test]
        public void Cascades_SessionDeletesPhaseSlotsAndCandidates_ButNotPhases()
        {
            InsertPhase("phase1", "P", 1, 5);
            InsertSession("s1", "S");
            InsertSlot("s1", "slot1", 0, "Warmup");
            InsertCandidate("slot1", "c1", 0, "phase1");

            Delete("session", "s1");

            AssertRowCount("phase_slot", 0);
            AssertRowCount("phase_slot_candidate", 0);
            AssertRowCount("phase", 1);
        }

        [Test]
        public void Cascades_ChoiceActionDeletesChoiceOptions()
        {
            InsertAction("choice1", "choice", true);
            InsertChoice("choice1", "Pick");
            InsertChoiceOption("opt1", "choice1", 0, "Go", null);

            Delete("action", "choice1");

            AssertRowCount("action_choice", 0);
            AssertRowCount("choice_option", 0);
        }

        [Test]
        public void Cascades_CardDeletesCardActionsAndCardTags_ButNotActionsOrTags()
        {
            InsertAction("a1", "debug", false);
            InsertTag("t1", "warm");
            InsertCard("card1", "C");
            InsertCardAction("card1", 0, "a1");
            InsertCardTag("card1", "t1", 0);

            Delete("card", "card1");

            AssertRowCount("card_action", 0);
            AssertRowCount("card_tag", 0);
            AssertRowCount("action", 1);
            AssertRowCount("tag", 1);
        }

        // --- Repeatable references (positive) ----------------------------------

        [Test]
        public void Allows_SameActionMultipleTimesOnCard()
        {
            InsertAction("a1", "debug", false);
            InsertCard("card1", "C");
            InsertCardAction("card1", 0, "a1");
            InsertCardAction("card1", 1, "a1");
            InsertCardAction("card1", 2, "a1");

            AssertRowCount("card_action", 3);
        }

        [Test]
        public void Allows_SameCardMultipleTimesInDeck()
        {
            InsertCard("card1", "C");
            InsertDeck("deck1", "D");
            InsertDeckCard("deck1", 0, "card1");
            InsertDeckCard("deck1", 1, "card1");

            AssertRowCount("card_deck_card", 2);
        }

        // --- Helpers -----------------------------------------------------------

        private void InsertSession(string id, string title) => Execute($"INSERT INTO session (id, title) VALUES ('{id}', '{title}');");
        private void InsertPhase(string id, string title, int min, int max) => Execute($"INSERT INTO phase (id, title, min_cards, max_cards) VALUES ('{id}', '{title}', {min}, {max});");
        private void InsertCard(string id, string title) => Execute($"INSERT INTO card (id, title) VALUES ('{id}', '{title}');");
        private void InsertTag(string id, string name) => Execute($"INSERT INTO tag (id, name) VALUES ('{id}', '{name}');");
        private void InsertAction(string id, string type, bool blocking) => Execute($"INSERT INTO action (id, action_type, is_blocking) VALUES ('{id}', '{type}', {(blocking ? 1 : 0)});");
        private void InsertChoice(string actionId, string prompt) => Execute($"INSERT INTO action_choice (action_id, prompt) VALUES ('{actionId}', '{prompt}');");
        private void InsertDeck(string id, string title) => Execute($"INSERT INTO card_deck (id, title) VALUES ('{id}', '{title}');");

        private void InsertSlot(string sessionId, string id, int ordinal, string title) =>
            Execute($"INSERT INTO phase_slot (id, session_id, ordinal, title) VALUES ('{id}', '{sessionId}', {ordinal}, '{title}');");

        private void InsertCandidate(string slotId, string id, int ordinal, string phaseId) =>
            Execute($"INSERT INTO phase_slot_candidate (id, phase_slot_id, ordinal, phase_id) VALUES ('{id}', '{slotId}', {ordinal}, '{phaseId}');");

        private void InsertCardAction(string cardId, int ordinal, string actionId) =>
            Execute($"INSERT INTO card_action (card_id, ordinal, action_id) VALUES ('{cardId}', {ordinal}, '{actionId}');");

        private void InsertCardTag(string cardId, string tagId, int ordinal) =>
            Execute($"INSERT INTO card_tag (card_id, tag_id, ordinal) VALUES ('{cardId}', '{tagId}', {ordinal});");

        private void InsertChoiceOption(string id, string choiceActionId, int ordinal, string label, string childActionId) =>
            Execute($"INSERT INTO choice_option (id, choice_action_id, ordinal, label, child_action_id) VALUES ('{id}', '{choiceActionId}', {ordinal}, '{label}', {(childActionId == null ? "NULL" : "'" + childActionId + "'")});");

        private void InsertDeckCard(string deckId, int ordinal, string cardId) =>
            Execute($"INSERT INTO card_deck_card (deck_id, ordinal, card_id) VALUES ('{deckId}', {ordinal}, '{cardId}');");

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
