using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite.Tests
{
    [TestFixture]
    public sealed class CardFolderRepositoryTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "gwb-card-folders-" + Guid.NewGuid().ToString("N") + ".db");
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

        [Test]
        public void Hierarchy_PersistsEmptyFolders_MovesGroups_AndRenamesDescendants()
        {
            CardFolderRepository.Create(_connection, "folder-root", "Scenes", null);
            CardFolderRepository.Create(_connection, "folder-child", "Opening", "folder-root");
            CardFolderRepository.Create(_connection, "folder-empty", "Unused", "folder-root");

            CardRepository.Create(_connection, new CardDefinition { Id = "card-a", Title = "A", FolderPath = "Scenes/Opening" });
            CardRepository.Create(_connection, new CardDefinition { Id = "card-b", Title = "B", FolderPath = "Scenes/Opening" });

            var folders = CardFolderRepository.Load(_connection);
            Assert.That(folders.Select(folder => folder.Path), Is.EquivalentTo(new[] { "Scenes", "Scenes/Opening", "Scenes/Unused" }));

            var command = new MoveCardsToFolderCommand(Open, new[] { "card-a", "card-b" }, "Scenes/Unused");
            command.Execute();
            var moved = GameContentSnapshotLoader.Load(_connection).Cards;
            Assert.That(moved.Single(card => card.Id == "card-a").FolderPath, Is.EqualTo("Scenes/Unused"));
            Assert.That(moved.Single(card => card.Id == "card-b").FolderPath, Is.EqualTo("Scenes/Unused"));

            CardFolderRepository.Rename(_connection, "folder-root", "Chapters");
            var renamed = GameContentSnapshotLoader.Load(_connection);
            Assert.That(renamed.CardFolders.Select(folder => folder.Path), Is.EquivalentTo(new[] { "Chapters", "Chapters/Opening", "Chapters/Unused" }));
            Assert.That(renamed.Cards.All(card => card.FolderPath == "Chapters/Unused"), Is.True);

            CardRepository.SetFolder(_connection, "card-a", "");
            CardRepository.SetFolder(_connection, "card-b", "");
            Assert.DoesNotThrow(() => CardFolderRepository.Delete(_connection, "folder-child"));
            Assert.DoesNotThrow(() => CardFolderRepository.Delete(_connection, "folder-empty"));
        }

        [Test]
        public void SubtreeDelete_CascadingRemovesFoldersAndCards_ThenUndoRestoresEverything()
        {
            CardFolderRepository.Create(_connection, "folder-root", "Scenes", null);
            CardFolderRepository.Create(_connection, "folder-child", "Opening", "folder-root");
            CardRepository.Create(_connection, new CardDefinition { Id = "card-a", Title = "A", FolderPath = "Scenes/Opening" });
            CardRepository.Create(_connection, new CardDefinition { Id = "card-b", Title = "B", FolderPath = "Scenes/Opening" });

            var command = new DeleteCardFolderTreeCommand(Open, "folder-root", deleteCards: true);
            command.Execute();

            var after = GameContentSnapshotLoader.Load(_connection);
            Assert.That(after.CardFolders, Is.Empty, "The whole subtree must be gone.");
            Assert.That(after.Cards, Is.Empty, "Cascade delete must remove contained cards.");

            command.Undo();

            var restored = GameContentSnapshotLoader.Load(_connection);
            Assert.That(restored.CardFolders.Select(folder => folder.Path),
                Is.EquivalentTo(new[] { "Scenes", "Scenes/Opening" }));
            Assert.That(restored.Cards.Select(card => card.Id), Is.EquivalentTo(new[] { "card-a", "card-b" }));
            Assert.That(restored.Cards.All(card => card.FolderPath == "Scenes/Opening"), Is.True);
            Assert.That(restored.Cards.All(card => card.Sequence != null && card.Sequence.Instances.Count > 0),
                Is.True, "Undo must restore each card with its owned sequence.");
        }

        [Test]
        public void SubtreeDelete_KeepCardsRelocatesThemToUnassigned_ThenUndoPutsThemBack()
        {
            CardFolderRepository.Create(_connection, "folder-root", "Scenes", null);
            CardFolderRepository.Create(_connection, "folder-child", "Opening", "folder-root");
            CardRepository.Create(_connection, new CardDefinition { Id = "card-a", Title = "A", FolderPath = "Scenes/Opening" });

            var command = new DeleteCardFolderTreeCommand(Open, "folder-root", deleteCards: false);
            command.Execute();

            var after = GameContentSnapshotLoader.Load(_connection);
            Assert.That(after.CardFolders, Is.Empty);
            Assert.That(after.Cards.Single().Id, Is.EqualTo("card-a"));
            Assert.That(after.Cards.Single().FolderPath, Is.EqualTo(""), "Kept cards must move to the unassigned root.");

            command.Undo();

            var restored = GameContentSnapshotLoader.Load(_connection);
            Assert.That(restored.CardFolders.Select(folder => folder.Path),
                Is.EquivalentTo(new[] { "Scenes", "Scenes/Opening" }));
            Assert.That(restored.Cards.Single().FolderPath, Is.EqualTo("Scenes/Opening"));
        }

        [Test]
        public void SubtreeDelete_CapturesNestedActionSequencesForUndo()
        {
            CardFolderRepository.Create(_connection, "folder-root", "Deep", null);
            var card = new CardDefinition { Id = "card-nested", Title = "Nested", FolderPath = "Deep" };
            card.Sequence.Instances.Add(new DialogInstanceDefinition { Id = "act-dialog", Text = "Hello", IsBlocking = true });
            card.Sequence.Instances.Add(new PromptChoiceInstanceDefinition { Id = "act-prompt", Prompt = "Choose" });
            CardRepository.Create(_connection, card);

            var command = new DeleteCardFolderTreeCommand(Open, "folder-root", deleteCards: true);
            command.Execute();
            Assert.That(GameContentSnapshotLoader.Load(_connection).Cards, Is.Empty);

            command.Undo();
            var restored = GameContentSnapshotLoader.Load(_connection).Cards.Single();
            Assert.That(restored.Sequence.Instances.Select(instance => instance.Id),
                Is.EqualTo(new[] { "act-dialog", "act-prompt" }));
            var prompt = (PromptChoiceInstanceDefinition)restored.Sequence.Instances[1];
            Assert.That(prompt.Prompt, Is.EqualTo("Choose"));
        }

        [Test]
        public void BatchMove_MovesCardsAndFolderSubtrees_ThenUndoRestoresOriginals()
        {
            CardFolderRepository.Create(_connection, "folder-a", "Alpha", null);
            CardFolderRepository.Create(_connection, "folder-b", "Beta", null);
            CardFolderRepository.Create(_connection, "folder-a-child", "Nested", "folder-a");
            CardRepository.Create(_connection, new CardDefinition { Id = "card-a", Title = "A", FolderPath = "Alpha/Nested" });
            CardRepository.Create(_connection, new CardDefinition { Id = "card-b", Title = "B", FolderPath = "Beta" });

            var command = new MoveCardSelectionCommand(Open,
                new[] { "card-b" }, new[] { "folder-a" }, "folder-b");
            command.Execute();

            var after = GameContentSnapshotLoader.Load(_connection);
            Assert.That(after.CardFolders.Select(folder => folder.Path),
                Is.EquivalentTo(new[] { "Beta", "Beta/Alpha", "Beta/Alpha/Nested" }));
            Assert.That(after.Cards.Single(card => card.Id == "card-a").FolderPath, Is.EqualTo("Beta/Alpha/Nested"));
            Assert.That(after.Cards.Single(card => card.Id == "card-b").FolderPath, Is.EqualTo("Beta"));

            command.Undo();

            var restored = GameContentSnapshotLoader.Load(_connection);
            Assert.That(restored.CardFolders.Select(folder => folder.Path),
                Is.EquivalentTo(new[] { "Alpha", "Alpha/Nested", "Beta" }));
            Assert.That(restored.Cards.Single(card => card.Id == "card-a").FolderPath, Is.EqualTo("Alpha/Nested"));
            Assert.That(restored.Cards.Single(card => card.Id == "card-b").FolderPath, Is.EqualTo("Beta"));
        }

        [Test]
        public void BatchMove_RejectsDroppingAFolderIntoItsOwnDescendant()
        {
            CardFolderRepository.Create(_connection, "folder-a", "Alpha", null);
            CardFolderRepository.Create(_connection, "folder-a-child", "Nested", "folder-a");

            var command = new MoveCardSelectionCommand(Open,
                new string[0], new[] { "folder-a" }, "folder-a-child");
            Assert.That(() => command.Execute(), Throws.InvalidOperationException);
        }

        [Test]
        public void BatchDelete_RemovesCardsAndFoldersInOneStep_ThenUndoRestoresAll()
        {
            CardFolderRepository.Create(_connection, "folder-a", "Alpha", null);
            CardRepository.Create(_connection, new CardDefinition { Id = "card-a", Title = "A", FolderPath = "Alpha" });
            var doomed = new CardDefinition { Id = "card-b", Title = "B" };
            doomed.Sequence.Instances.Add(new DialogInstanceDefinition { Id = "act-b", Text = "Bye", IsBlocking = true });
            CardRepository.Create(_connection, doomed);

            var command = new DeleteCardSelectionCommand(Open,
                new[] { "card-b" }, new[] { "folder-a" }, folderDeleteCards: true);
            command.Execute();

            var after = GameContentSnapshotLoader.Load(_connection);
            Assert.That(after.CardFolders, Is.Empty);
            Assert.That(after.Cards.Select(card => card.Id), Is.Empty);

            command.Undo();

            var restored = GameContentSnapshotLoader.Load(_connection);
            Assert.That(restored.CardFolders.Select(folder => folder.Path), Is.EquivalentTo(new[] { "Alpha" }));
            Assert.That(restored.Cards.Select(card => card.Id), Is.EquivalentTo(new[] { "card-a", "card-b" }));
            Assert.That(restored.Cards.Single(card => card.Id == "card-b").Sequence.Instances.Count, Is.EqualTo(1),
                "Undo must restore the authored sequence, not the default pair.");
        }

        [Test]
        public void BatchDuplicate_CopiesCardsAndFolderSubtrees_ThenUndoRemovesCopies()
        {
            CardFolderRepository.Create(_connection, "folder-a", "Alpha", null);
            CardFolderRepository.Create(_connection, "folder-a-child", "Nested", "folder-a");
            CardRepository.Create(_connection, new CardDefinition { Id = "card-a", Title = "A", FolderPath = "Alpha/Nested" });
            var standalone = new CardDefinition { Id = "card-b", Title = "B" };
            standalone.Sequence.Instances.Add(new DialogInstanceDefinition { Id = "act-b", Text = "Hello", IsBlocking = true });
            CardRepository.Create(_connection, standalone);

            var command = new DuplicateCardSelectionCommand(Open,
                new[] { "card-b" }, new[] { "folder-a" });
            command.Execute();

            var after = GameContentSnapshotLoader.Load(_connection);
            Assert.That(after.CardFolders.Select(folder => folder.Path),
                Is.EquivalentTo(new[] { "Alpha", "Alpha/Nested", "Alpha (copy)", "Alpha (copy)/Nested" }));
            Assert.That(after.Cards.Count(card => card.Title == "A" && card.FolderPath == "Alpha (copy)/Nested"), Is.EqualTo(1));
            Assert.That(after.Cards.Count(card => card.Title == "B (copy)"), Is.EqualTo(1),
                "Standalone card copies get the (copy) suffix.");
            Assert.That(command.NewCardIds.Count, Is.EqualTo(2), "Two new cards: the folder's card and the standalone.");
            Assert.That(command.NewFolderIds.Count, Is.EqualTo(1));

            // The folder's copied card kept its sequence; the standalone copy kept its authored instance.
            var copiedStandalone = after.Cards.Single(card => card.Title == "B (copy)");
            Assert.That(copiedStandalone.Sequence.Instances.Count, Is.EqualTo(1));
            Assert.That(copiedStandalone.Sequence.Instances[0], Is.InstanceOf<DialogInstanceDefinition>());

            command.Undo();

            var undone = GameContentSnapshotLoader.Load(_connection);
            Assert.That(undone.CardFolders.Select(folder => folder.Path), Is.EquivalentTo(new[] { "Alpha", "Alpha/Nested" }));
            Assert.That(undone.Cards.Select(card => card.Id), Is.EquivalentTo(new[] { "card-a", "card-b" }));
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection("Data Source=" + _dbPath);
            connection.Open();
            ConnectionInitializer.Initialize(connection);
            return connection;
        }
    }
}
