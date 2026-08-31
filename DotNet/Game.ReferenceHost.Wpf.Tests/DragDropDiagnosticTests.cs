using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.ReferenceHost.Wpf;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    /// <summary>
    /// Regression guard for the card-tree drag/drop release path. A real
    /// MainWindow is shown, the Cards library is populated via the in-memory
    /// content snapshot, and the drop-target resolver (FindCardFolderNode) is
    /// exercised over real folder and card row containers.
    /// </summary>
    [TestFixture]
    public class CardTreeDragDropTests
    {
        private const string FolderId = "folder-alpha";
        private const string CardId = "card-1";
        private const string FolderInFolderId = "folder-alpha-sub";

        [Test, Apartment(ApartmentState.STA)]
        public void DropTarget_Resolves_ExactlyLikeReleaseHittest()
        {
            var tree = BuildSeededTree(out var window);
            try
            {
                // Folder row resolves to itself (Move target) — release path must accept.
                var folderItem = FindContainer(tree, node => node.Id == FolderId && !node.IsCard);
                Assert.That(folderItem, Is.Not.Null, "folder row must materialize a container");
                var folderTarget = ResolveFolderNode(folderItem);
                Assert.That(folderTarget, Is.Not.Null, "folder row must resolve to a drop target");
                Assert.That(folderTarget.Id, Is.EqualTo(FolderId));

                // Nested folder row resolves to the nearest folder, not an ancestor.
                var subItem = FindContainer(tree, node => node.Id == FolderInFolderId && !node.IsCard);
                Assert.That(subItem, Is.Not.Null, "nested folder row must materialize a container");
                var subTarget = ResolveFolderNode(subItem);
                Assert.That(subTarget, Is.Not.Null);
                Assert.That(subTarget.Id, Is.EqualTo(FolderInFolderId));

                // Card row resolves to nothing: dropping onto a card must NOT collapse
                // onto its containing folder (which would move the card by mistake).
                var cardItem = FindContainer(tree, node => node.Id == CardId && node.IsCard);
                Assert.That(cardItem, Is.Not.Null, "card row must materialize a container");
                Assert.That(ResolveFolderNode(cardItem), Is.Null,
                    "dropping onto a card row must not resolve to its parent folder");
            }
            finally
            {
                window.Close();
            }
        }

        private static CardFolderTreeNode ResolveFolderNode(TreeViewItem container)
        {
            var method = typeof(MainWindow).GetMethod("FindCardFolderNode",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "FindCardFolderNode must exist");
            return method.Invoke(null, new object[] { container }) as CardFolderTreeNode;
        }

        private static TreeView BuildSeededTree(out MainWindow window)
        {
            EnsureApplication();
            window = new MainWindow();
            window.Show();

            SeedContent(window);
            ((Button)window.FindName("CardsLibraryTabButton"))?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var tree = (TreeView)window.FindName("CardFolderTree");
            Assert.That(tree, Is.Not.Null, "CardFolderTree must exist");

            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            ExpandAll(tree);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            return tree;
        }

        private static void SeedContent(MainWindow window)
        {
            var vm = typeof(MainWindow).GetField("_vm",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(window);
            var content = vm?.GetType().GetProperty("Content")?.GetValue(vm);
            Assert.That(content, Is.Not.Null, "window content snapshot must be available");

            var folders = content.GetType().GetProperty("CardFolders")?.GetValue(content) as List<CardFolderDefinition>;
            var cards = content.GetType().GetProperty("Cards")?.GetValue(content) as List<CardDefinition>;
            Assert.That(folders, Is.Not.Null);
            Assert.That(cards, Is.Not.Null);

            if (cards.Any(c => c.Id == CardId)) return; // already seeded by a prior test in this process

            folders.Add(new CardFolderDefinition { Id = FolderId, Name = "alpha", Path = "alpha" });
            folders.Add(new CardFolderDefinition { Id = FolderInFolderId, Name = "alpha-sub", Path = "alpha/alpha-sub" });
            cards.Add(new CardDefinition { Id = CardId, Title = "Card 1", FolderPath = "alpha" });
        }

        private static TreeViewItem FindContainer(ItemsControl root, Func<CardFolderTreeNode, bool> match)
        {
            for (var i = 0; i < root.Items.Count; i++)
            {
                var item = root.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (item == null) continue;
                if (item.DataContext is CardFolderTreeNode node && match(node)) return item;
                var child = FindContainer(item, match);
                if (child != null) return child;
            }
            return null;
        }

        private static void ExpandAll(ItemsControl owner)
        {
            for (var i = 0; i < owner.Items.Count; i++)
            {
                var item = owner.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (item == null) continue;
                item.IsExpanded = true;
                item.UpdateLayout();
                ExpandAll(item);
            }
        }

        private static void EnsureApplication()
        {
            if (Application.Current == null)
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Game.ReferenceHost.Wpf;component/DarkControls.xaml")
                });
            }
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
        }
    }
}