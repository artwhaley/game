using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 03: Performance Tags, Conversation Performance Events and the
    /// Perform action subtype persist through the existing typed repositories and
    /// reconstruct exactly through the shared snapshot loader. Retirement keeps
    /// identity; deletion is blocked while referenced.
    /// </summary>
    [TestFixture]
    public class PerformancePersistenceTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "gwb-perf-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);
        }

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            _connection.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void V12_CreatesPerformanceTables()
        {
            foreach (var table in new[]
                     {
                         "performance_tag_definition", "conversation_performance_event",
                         "performance_event_tag", "performance_event_allowed_anchor",
                         "performance_event_allowed_posture", "action_instance_perform",
                     })
            {
                Assert.That(TableExists(table), Is.True, "v12 must create " + table);
            }
        }

        [Test]
        public void PerformanceTag_And_Event_RoundTrip()
        {
            CreateTags("tag-playful", "tag-tease", "tag-stern");
            PerformanceCatalogRepository.SaveEvent(_connection, new ConversationPerformanceEventDefinition
            {
                Id = "evt-tease",
                Name = "Playful Tease",
                RequireAllTags = true,
                StagingPolicy = PerformanceStagingPolicy.DifferentLocation,
                NamedAnchorId = "",
                RefreshAtDialogueStart = true,
                SortOrder = 3,
            });
            var stored = PerformanceCatalogRepository.ListEvents(_connection).Find(e => e.Id == "evt-tease");
            stored.PerformanceTagIds.Add("tag-playful");
            stored.PerformanceTagIds.Add("tag-tease");
            stored.AllowedAnchorIds.Add("anchor-chair");
            stored.AllowedPostureIds.Add("sitting");
            PerformanceCatalogRepository.SaveEvent(_connection, stored);

            var content = GameContentSnapshotLoader.Load(_connection);
            var reloaded = content.PerformanceEvents.Find(e => e.Id == "evt-tease");
            Assert.IsNotNull(reloaded);
            Assert.AreEqual("Playful Tease", reloaded.Name);
            Assert.IsTrue(reloaded.RequireAllTags);
            Assert.AreEqual(PerformanceStagingPolicy.DifferentLocation, reloaded.StagingPolicy);
            Assert.IsTrue(reloaded.RefreshAtDialogueStart);
            Assert.AreEqual(3, reloaded.SortOrder);
            CollectionAssert.AreEquivalent(new[] { "tag-playful", "tag-tease" }, reloaded.PerformanceTagIds);
            CollectionAssert.AreEqual(new[] { "anchor-chair" }, reloaded.AllowedAnchorIds);
            CollectionAssert.AreEqual(new[] { "sitting" }, reloaded.AllowedPostureIds);
            Assert.AreEqual(3, content.PerformanceTags.Count);
        }

        [Test]
        public void PerformAction_RoundTripsThroughCardAndLoader()
        {
            CreateTags("tag-playful");
            PerformanceCatalogRepository.SaveEvent(_connection, new ConversationPerformanceEventDefinition
            {
                Id = "evt", Name = "Event", RequireAllTags = true,
            });
            var stored = PerformanceCatalogRepository.ListEvents(_connection).Find(e => e.Id == "evt");
            stored.PerformanceTagIds.Add("tag-playful");
            PerformanceCatalogRepository.SaveEvent(_connection, stored);

            var card = new CardDefinition
            {
                Id = "card-perform",
                Title = "Performance Card",
                Sequence = new ActionSequenceDefinition { Id = "seq-perform" },
            };
            card.Sequence.Instances.Add(new PerformInstanceDefinition { Id = "perform-1", EventId = "evt", IsBlocking = true });
            card.Sequence.Instances.Add(new WaitForContinueInstanceDefinition { Id = "wait-1", IsBlocking = true });
            CardRepository.Create(_connection, card);

            var content = GameContentSnapshotLoader.Load(_connection);
            var reloaded = content.Cards.Find(c => c.Id == "card-perform");
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(2, reloaded.Sequence.Instances.Count);
            var perform = reloaded.Sequence.Instances[0] as PerformInstanceDefinition;
            Assert.IsNotNull(perform);
            Assert.AreEqual("evt", perform.EventId);
            Assert.IsTrue(perform.IsBlocking, "Perform is always blocking");
        }

        [Test]
        public void RetiredTag_RetainsIdentityAndResolution()
        {
            CreateTags("tag-retired");
            PerformanceCatalogRepository.SetRetired(_connection, "tag-retired", true);

            var tags = PerformanceCatalogRepository.ListTags(_connection);
            var tag = tags.Find(t => t.Id == "tag-retired");
            Assert.IsNotNull(tag);
            Assert.IsTrue(tag.IsRetired);

            var content = GameContentSnapshotLoader.Load(_connection);
            Assert.IsTrue(content.PerformanceTags.Find(t => t.Id == "tag-retired").IsRetired);
            Assert.DoesNotThrow(() => new TruthCardGame.Core.ContentCatalog(content).PerformanceTagById("tag-retired"));
        }

        [Test]
        public void DeleteEventIfUnused_BlocksWhenReferencedByPerform()
        {
            CreateTags("tag-x");
            PerformanceCatalogRepository.SaveEvent(_connection, new ConversationPerformanceEventDefinition
                { Id = "evt-used", Name = "Used" });
            var stored = PerformanceCatalogRepository.ListEvents(_connection).Find(e => e.Id == "evt-used");
            stored.PerformanceTagIds.Add("tag-x");
            PerformanceCatalogRepository.SaveEvent(_connection, stored);

            var card = new CardDefinition { Id = "card", Title = "C", Sequence = new ActionSequenceDefinition { Id = "seq" } };
            card.Sequence.Instances.Add(new PerformInstanceDefinition { Id = "p", EventId = "evt-used", IsBlocking = true });
            CardRepository.Create(_connection, card);

            Assert.AreEqual(1, PerformanceCatalogRepository.GetEventUsage(_connection, "evt-used"));
            Assert.Throws<InvalidOperationException>(
                () => PerformanceCatalogRepository.DeleteEventIfUnused(_connection, "evt-used"));
        }

        [Test]
        public void DeleteTagIfUnused_BlocksWhenReferencedByEvent()
        {
            CreateTags("tag-ref");
            PerformanceCatalogRepository.SaveEvent(_connection, new ConversationPerformanceEventDefinition
                { Id = "evt", Name = "Event" });
            var stored = PerformanceCatalogRepository.ListEvents(_connection).Find(e => e.Id == "evt");
            stored.PerformanceTagIds.Add("tag-ref");
            PerformanceCatalogRepository.SaveEvent(_connection, stored);

            Assert.AreEqual(1, PerformanceCatalogRepository.GetTagUsage(_connection, "tag-ref"));
            Assert.Throws<InvalidOperationException>(
                () => PerformanceCatalogRepository.DeleteTagIfUnused(_connection, "tag-ref"));

            // An unused tag can be deleted outright.
            CreateTags("tag-free");
            Assert.DoesNotThrow(() => PerformanceCatalogRepository.DeleteTagIfUnused(_connection, "tag-free"));
        }

        [Test]
        public void Writer_RejectsPersistingPerformReferencingUnknownEvent()
        {
            var card = new CardDefinition { Id = "card-bad", Title = "Bad", Sequence = new ActionSequenceDefinition { Id = "seq-bad" } };
            card.Sequence.Instances.Add(new PerformInstanceDefinition { Id = "p", EventId = "evt-missing", IsBlocking = true });

            // Under normal authoring the FK on action_instance_perform.event_id
            // makes a dangling reference unrepresentable at write time.
            Assert.Throws<SqliteException>(() => CardRepository.Create(_connection, card));
        }

        [Test]
        public void Loader_RejectsPerformReferencingUnknownEvent()
        {
            // Externally authored or legacy databases may have been written with
            // SQLite foreign-key enforcement off. The shared loader is still the
            // last line of defense and must fail loud instead of handing Core a
            // dangling Performance Event reference.
            Sql.Execute(_connection, null, "PRAGMA foreign_keys = OFF;");
            try
            {
                var card = new CardDefinition { Id = "card-bad", Title = "Bad", Sequence = new ActionSequenceDefinition { Id = "seq-bad" } };
                card.Sequence.Instances.Add(new PerformInstanceDefinition { Id = "p", EventId = "evt-missing", IsBlocking = true });
                CardRepository.Create(_connection, card);
            }
            finally
            {
                Sql.Execute(_connection, null, "PRAGMA foreign_keys = ON;");
            }

            var failure = Assert.Throws<InvalidOperationException>(() => GameContentSnapshotLoader.Load(_connection));
            StringAssert.Contains("unknown Performance Event", failure.Message);
        }

        [Test]
        public void UpdateEvent_ReplacesRelationsRatherThanAccumulating()
        {
            CreateTags("tag-a", "tag-b");
            PerformanceCatalogRepository.SaveEvent(_connection, new ConversationPerformanceEventDefinition
                { Id = "evt", Name = "Event" });
            var stored = PerformanceCatalogRepository.ListEvents(_connection).Find(e => e.Id == "evt");
            stored.PerformanceTagIds.Add("tag-a");
            stored.AllowedPostureIds.Add("standing");
            PerformanceCatalogRepository.SaveEvent(_connection, stored);

            stored.PerformanceTagIds.Clear();
            stored.PerformanceTagIds.Add("tag-b");
            stored.AllowedPostureIds.Clear();
            stored.AllowedPostureIds.Add("sitting");
            PerformanceCatalogRepository.SaveEvent(_connection, stored);

            var reloaded = PerformanceCatalogRepository.ListEvents(_connection).Find(e => e.Id == "evt");
            CollectionAssert.AreEqual(new[] { "tag-b" }, reloaded.PerformanceTagIds);
            CollectionAssert.AreEqual(new[] { "sitting" }, reloaded.AllowedPostureIds);
        }

        private void CreateTags(params string[] ids)
        {
            foreach (var id in ids)
            {
                PerformanceCatalogRepository.CreateTag(_connection, new PerformanceTagDefinition { Id = id, Title = id });
            }
        }

        private bool TableExists(string name)
        {
            var found = false;
            Sql.QueryAll(_connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = @n;",
                reader => found = Convert.ToInt64(reader.GetValue(0)) > 0, ("n", name));
            return found;
        }
    }
}
