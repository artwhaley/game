using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    [TestFixture]
    public class AuthoringRepositoryTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "sqlite-authoring-" + Guid.NewGuid().ToString("N") + ".db");
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

        private static string Id() => "id-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        [Test]
        public void CreateEditReload_Session()
        {
            var id = Id();
            SessionRepository.Create(_connection, id, "Night One", new[] { "intense", "new" });
            var created = SessionRepository.Get(_connection, id);
            Assert.AreEqual("Night One", created.Title);
            CollectionAssert.AreEqual(new[] { "intense", "new" }, created.Tags.ToArray());

            SessionRepository.UpdateTitle(_connection, id, "Night Two");
            SessionRepository.ReplaceTags(_connection, id, new[] { "relaxing" });

            var reloaded = SessionRepository.Get(_connection, id);
            Assert.AreEqual("Night Two", reloaded.Title);
            CollectionAssert.AreEqual(new[] { "relaxing" }, reloaded.Tags.ToArray());
        }

        [Test]
        public void SharedPhase_ReferencedByMultipleSlots_Survives()
        {
            var phaseId = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = phaseId, Title = "Warm Up", MinCards = 2, MaxCards = 3 });

            var s1 = Id();
            var s2 = Id();
            SessionRepository.Create(_connection, s1, "S1", null);
            SessionRepository.Create(_connection, s2, "S2", null);
            var slot1 = Id();
            var slot2 = Id();
            PhaseSlotRepository.Create(_connection, s1, slot1, "Warmup");
            PhaseSlotRepository.Create(_connection, s2, slot2, "Warmup");
            PhaseSlotRepository.AddCandidate(_connection, slot1, Id(), phaseId);
            PhaseSlotRepository.AddCandidate(_connection, slot2, Id(), phaseId);

            var slotsOfS1 = SessionRepository.ListPhaseSlots(_connection, s1);
            var slotsOfS2 = SessionRepository.ListPhaseSlots(_connection, s2);
            Assert.AreEqual(phaseId, slotsOfS1[0].Candidates[0].PhaseId);
            Assert.AreEqual(phaseId, slotsOfS2[0].Candidates[0].PhaseId);
            Assert.AreEqual("Warm Up", PhaseRepository.Get(_connection, phaseId).Title);
        }

        [Test]
        public void ReorderSlots_ProducesNewOrder_WithoutConstraintViolation()
        {
            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "S", null);
            var a = Id();
            var b = Id();
            var c = Id();
            PhaseSlotRepository.Create(_connection, sessionId, a, "A");
            PhaseSlotRepository.Create(_connection, sessionId, b, "B");
            PhaseSlotRepository.Create(_connection, sessionId, c, "C");

            PhaseSlotRepository.Reorder(_connection, sessionId, new[] { c, a, b });

            CollectionAssert.AreEqual(
                new[] { c, a, b },
                SessionRepository.ListPhaseSlots(_connection, sessionId).Select(s => s.Id).ToArray());
        }

        [Test]
        public void ReorderCandidates_ProducesNewOrder()
        {
            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "S", null);
            var slotId = Id();
            PhaseSlotRepository.Create(_connection, sessionId, slotId, "Resolution");

            var p1 = Id();
            var p2 = Id();
            var p3 = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = p1, Title = "P1" });
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = p2, Title = "P2" });
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = p3, Title = "P3" });
            var c1 = Id();
            var c2 = Id();
            var c3 = Id();
            PhaseSlotRepository.AddCandidate(_connection, slotId, c1, p1);
            PhaseSlotRepository.AddCandidate(_connection, slotId, c2, p2);
            PhaseSlotRepository.AddCandidate(_connection, slotId, c3, p3);

            PhaseSlotRepository.ReorderCandidates(_connection, slotId, new[] { c3, c1, c2 });

            CollectionAssert.AreEqual(
                new[] { c3, c1, c2 },
                PhaseSlotRepository.ListCandidates(_connection, slotId).Select(c => c.Id).ToArray());
        }

        [Test]
        public void DeleteInUsePhase_IsBlocked_UnreferencedPhase_Deletes()
        {
            var inUse = Id();
            var free = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = inUse, Title = "In Use" });
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = free, Title = "Free" });

            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "S", null);
            var slotId = Id();
            PhaseSlotRepository.Create(_connection, sessionId, slotId, "Slot");
            PhaseSlotRepository.AddCandidate(_connection, slotId, Id(), inUse);

            var ex = Assert.Throws<InvalidOperationException>(() => PhaseRepository.Delete(_connection, inUse));
            StringAssert.Contains("cannot be deleted", ex.Message);

            PhaseRepository.Delete(_connection, free);
            Assert.Throws<InvalidOperationException>(() => PhaseRepository.Get(_connection, free));
        }

        [Test]
        public void DeleteSession_RemovesSlotsAndCandidates_ButNotPhases()
        {
            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "S", null);
            var phaseId = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = phaseId, Title = "P" });
            var slotId = Id();
            PhaseSlotRepository.Create(_connection, sessionId, slotId, "Slot");
            PhaseSlotRepository.AddCandidate(_connection, slotId, Id(), phaseId);

            SessionRepository.Delete(_connection, sessionId);

            Assert.IsEmpty(SessionRepository.ListPhaseSlots(_connection, sessionId));
            Assert.AreEqual("P", PhaseRepository.Get(_connection, phaseId).Title);
        }

        [Test]
        public void Phase_TagReplacement_IsTransactionalAndOrdered()
        {
            var phaseId = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition
            {
                Id = phaseId,
                Title = "P",
                MinCards = 2,
                MaxCards = 4,
                MustIncludeTags = { "solo", "truth" }
            });

            PhaseRepository.ReplaceMustIncludeTags(_connection, phaseId, new[] { "party", "dare", "solo" });

            var reloaded = PhaseRepository.Get(_connection, phaseId);
            CollectionAssert.AreEqual(new[] { "party", "dare", "solo" }, reloaded.MustIncludeTags.ToArray());
        }

        [Test]
        public void Ids_ArePreserved_AcrossCreateAndReload()
        {
            var sessionId = "session-preserve-1";
            var slotId = "slot-preserve-1";
            var candidateId = "candidate-preserve-1";
            var phaseId = "phase-preserve-1";

            PhaseRepository.Create(_connection, new PhaseDefinition { Id = phaseId, Title = "P" });
            SessionRepository.Create(_connection, sessionId, "S", null);
            PhaseSlotRepository.Create(_connection, sessionId, slotId, "Slot");
            PhaseSlotRepository.AddCandidate(_connection, slotId, candidateId, phaseId);

            // Get() returns the session row + tags (slots load via ListPhaseSlots).
            Assert.AreEqual("S", SessionRepository.Get(_connection, sessionId).Title);
            var slots = SessionRepository.ListPhaseSlots(_connection, sessionId);
            Assert.AreEqual(slotId, slots[0].Id);
            Assert.AreEqual(candidateId, slots[0].Candidates[0].Id);
            Assert.AreEqual(phaseId, slots[0].Candidates[0].PhaseId);
        }

        [Test]
        public void FakeUnityExtensionRow_SurvivesUnrelatedEdits()
        {
            Execute("CREATE TABLE unity_fake_extension (id TEXT PRIMARY KEY, payload TEXT NOT NULL);");
            Execute("INSERT INTO unity_fake_extension (id, payload) VALUES ('b1', 'binding');");

            var sessionId = Id();
            SessionRepository.Create(_connection, sessionId, "S", new[] { "intense" });
            SessionRepository.ReplaceTags(_connection, sessionId, new[] { "relaxing" });
            var phaseId = Id();
            PhaseRepository.Create(_connection, new PhaseDefinition { Id = phaseId, Title = "P", MustIncludeTags = { "solo" } });
            PhaseRepository.ReplaceMustExcludeTags(_connection, phaseId, new[] { "dare" });
            var slotId = Id();
            PhaseSlotRepository.Create(_connection, sessionId, slotId, "Slot");
            PhaseSlotRepository.Reorder(_connection, sessionId, new[] { slotId });
            SessionRepository.Delete(_connection, sessionId);

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT payload FROM unity_fake_extension WHERE id = 'b1';";
                Assert.AreEqual("binding", command.ExecuteScalar());
            }
        }

        [Test]
        public void StableIds_AreNonEmptyAndUnique()
        {
            var a = StableIds.New();
            var b = StableIds.New();
            Assert.IsNotEmpty(a);
            Assert.IsNotEmpty(b);
            Assert.AreNotEqual(a, b);
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
