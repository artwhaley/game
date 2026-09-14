using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 03: the Performance vocabulary is authored through the same
    /// semantic undo/redo command layer as every other catalog. Tags keep
    /// identity across rename and retirement, events round-trip their full form,
    /// and delete-undo restores relations rather than just the title.
    /// </summary>
    [TestFixture]
    public class PerformanceCatalogCommandTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;
        private AuthoringCommandStack _stack;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-perfcmd-{Guid.NewGuid():N}.db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            _connection.Open();
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);
            _stack = new AuthoringCommandStack();
        }

        [TearDown]
        public void Dispose()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private Func<DbConnection> Conn => () =>
        {
            var connection = new SqliteConnection("Data Source=" + _dbPath);
            connection.Open();
            ConnectionInitializer.Initialize(connection);
            return connection;
        };

        private static CatalogEntryEdit TagEdit(string id, string title, bool retired = false)
        {
            return new CatalogEntryEdit
            {
                Kind = CatalogKinds.PerformanceTag, Id = id, Title = title, IsRetired = retired,
            };
        }

        private static CatalogEntryEdit EventEdit(string id, string name, params string[] tagIds)
        {
            var edit = new CatalogEntryEdit
            {
                Kind = CatalogKinds.PerformanceEvent,
                Id = id,
                Title = name,
                RequireAllTags = false,
                StagingPolicy = PerformanceStagingPolicy.DifferentLocation,
                NamedAnchorId = "",
                RefreshAtDialogueStart = true,
                SortOrder = 2,
            };
            edit.PerformanceTagIds.AddRange(tagIds);
            edit.AllowedAnchorIds.Add("anchor-chair");
            edit.AllowedPostureIds.Add("sitting");
            return edit;
        }

        [Test]
        public void CreateTag_IsImmediatelyVisibleThroughTheCatalogKinds()
        {
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful"));

            var tag = CatalogEntryEdit.FromPerformanceTag(PerformanceCatalogRepository.ReadTag(_connection, "tag-playful"));
            Assert.IsNotNull(tag);
            Assert.AreEqual("Playful", tag.Title);
            Assert.IsFalse(tag.IsRetired);
        }

        [Test]
        public void SaveTag_RenameAndRetireUndoBackToTheOriginalRow()
        {
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful"));
            var original = CatalogEntryEdit.FromPerformanceTag(PerformanceCatalogRepository.ReadTag(_connection, "tag-playful"));

            var renamed = TagEdit("tag-playful", "Playful Tease", retired: true);
            _stack.PushOrMerge(new UpdateCatalogEntryCommand(Conn, original, renamed));

            var stored = PerformanceCatalogRepository.ReadTag(_connection, "tag-playful");
            Assert.AreEqual("Playful Tease", stored.Title);
            Assert.IsTrue(stored.IsRetired);

            _stack.Undo();
            var restored = PerformanceCatalogRepository.ReadTag(_connection, "tag-playful");
            Assert.AreEqual("Playful", restored.Title);
            Assert.IsFalse(restored.IsRetired);

            _stack.Redo();
            Assert.IsTrue(PerformanceCatalogRepository.ReadTag(_connection, "tag-playful").IsRetired);
        }

        [Test]
        public void SaveEvent_RoundTripsTheWholeFormThroughUndoAndRedo()
        {
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful"));
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceEvent, "evt-tease", "Tease"));

            var created = CatalogEntryEdit.FromPerformanceEvent(PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease"));
            Assert.IsTrue(created.RequireAllTags, "a new event defaults to ALL tags");
            Assert.IsTrue(created.RefreshAtDialogueStart, "automatic dialogue refresh defaults on");
            Assert.AreEqual(PerformanceStagingPolicy.Stay, created.StagingPolicy);

            _stack.PushOrMerge(new UpdateCatalogEntryCommand(Conn, created, EventEdit("evt-tease", "Playful Tease", "tag-playful")));

            var edited = PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease");
            Assert.AreEqual("Playful Tease", edited.Name);
            Assert.IsFalse(edited.RequireAllTags);
            Assert.AreEqual(PerformanceStagingPolicy.DifferentLocation, edited.StagingPolicy);
            Assert.AreEqual(2, edited.SortOrder);
            CollectionAssert.AreEqual(new[] { "tag-playful" }, edited.PerformanceTagIds);
            CollectionAssert.AreEqual(new[] { "anchor-chair" }, edited.AllowedAnchorIds);
            CollectionAssert.AreEqual(new[] { "sitting" }, edited.AllowedPostureIds);

            _stack.Undo();
            var restored = PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease");
            Assert.AreEqual("Tease", restored.Name);
            Assert.IsTrue(restored.RequireAllTags);
            Assert.AreEqual(PerformanceStagingPolicy.Stay, restored.StagingPolicy);
            Assert.IsEmpty(restored.PerformanceTagIds);
            Assert.IsEmpty(restored.AllowedAnchorIds);

            _stack.Redo();
            Assert.AreEqual("Playful Tease", PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease").Name);
        }

        [Test]
        public void DeleteEvent_UndoRestoresRelationsNotJustTheName()
        {
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful"));
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceEvent, "evt-tease", "Tease"));
            var created = CatalogEntryEdit.FromPerformanceEvent(PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease"));
            _stack.PushOrMerge(new UpdateCatalogEntryCommand(Conn, created, EventEdit("evt-tease", "Playful Tease", "tag-playful")));
            var full = CatalogEntryEdit.FromPerformanceEvent(PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease"));

            _stack.PushOrMerge(new DeleteCatalogEntryCommand(Conn, CatalogKinds.PerformanceEvent, "evt-tease", "Playful Tease", full));
            Assert.IsNull(PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease"));

            _stack.Undo();
            var restored = PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease");
            Assert.IsNotNull(restored);
            Assert.AreEqual("Playful Tease", restored.Name);
            CollectionAssert.AreEqual(new[] { "tag-playful" }, restored.PerformanceTagIds);
            CollectionAssert.AreEqual(new[] { "sitting" }, restored.AllowedPostureIds);
        }

        [Test]
        public void DeleteTag_IsBlockedWhileAnEventStillReferencesIt()
        {
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful"));
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceEvent, "evt-tease", "Tease"));
            var created = CatalogEntryEdit.FromPerformanceEvent(PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease"));
            _stack.PushOrMerge(new UpdateCatalogEntryCommand(Conn, created, EventEdit("evt-tease", "Tease", "tag-playful")));

            Assert.Throws<InvalidOperationException>(
                () => _stack.PushOrMerge(new DeleteCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful")));

            // Retiring is the supported path while references exist: identity and
            // resolution survive, and the event keeps its tag.
            var tag = CatalogEntryEdit.FromPerformanceTag(PerformanceCatalogRepository.ReadTag(_connection, "tag-playful"));
            _stack.PushOrMerge(new UpdateCatalogEntryCommand(Conn, tag, TagEdit("tag-playful", "Playful", retired: true)));

            Assert.IsTrue(PerformanceCatalogRepository.ReadTag(_connection, "tag-playful").IsRetired);
            Assert.AreEqual(1, PerformanceCatalogRepository.GetTagUsage(_connection, "tag-playful"));
            Assert.That(PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease").PerformanceTagIds,
                Is.EqualTo(new[] { "tag-playful" }));
        }

        [Test]
        public void RenameTag_KeepsStableIdentitySoUnityIngredientsStillResolve()
        {
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful"));
            _stack.PushOrMerge(new RenameCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful", "Mischievous"));

            Assert.AreEqual("Mischievous", PerformanceCatalogRepository.ReadTag(_connection, "tag-playful").Title);
            CollectionAssert.Contains(
                PerformanceCatalogRepository.ListTags(_connection).Select(tag => tag.Id), "tag-playful");

            _stack.Undo();
            Assert.AreEqual("Playful", PerformanceCatalogRepository.ReadTag(_connection, "tag-playful").Title);
        }

        [Test]
        public void RenameEvent_KeepsStableIdentityAndRelations()
        {
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceTag, "tag-playful", "Playful"));
            _stack.PushOrMerge(new CreateCatalogEntryCommand(Conn, CatalogKinds.PerformanceEvent, "evt-tease", "Tease"));
            var created = CatalogEntryEdit.FromPerformanceEvent(PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease"));
            _stack.PushOrMerge(new UpdateCatalogEntryCommand(Conn, created, EventEdit("evt-tease", "Tease", "tag-playful")));

            _stack.PushOrMerge(new RenameCatalogEntryCommand(Conn, CatalogKinds.PerformanceEvent, "evt-tease", "Tease", "Playful Tease"));

            var renamed = PerformanceCatalogRepository.ReadEvent(_connection, "evt-tease");
            Assert.AreEqual("Playful Tease", renamed.Name);
            CollectionAssert.AreEqual(new[] { "tag-playful" }, renamed.PerformanceTagIds);
        }
    }
}
