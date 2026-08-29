using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using Nodify;
using TruthCardGame.Content;
using TruthCardGame.Core;
using TruthCardGame.ReferenceHost.Wpf;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GraphLayoutBindingTests
    {
        private static Application EnsureApplication()
        {
            if (Application.Current != null) return Application.Current;
            var app = new App();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new System.Uri("pack://application:,,,/Nodify;component/Themes/Dark.xaml")
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new System.Uri("pack://application:,,,/Game.ReferenceHost.Wpf;component/DarkControls.xaml")
            });
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            return app;
        }

        [Test]
        public void BothGraphEditorsUseTheSharedContainerStyle()
        {
            EnsureApplication();
            var window = new MainWindow();
            try
            {
                var style = (Style)window.Resources["GraphItemContainerStyle"];
                Assert.That(style, Is.Not.Null);
                Assert.That(((NodifyEditor)window.FindName("SessionEditor")).ItemContainerStyle, Is.SameAs(style));
                Assert.That(((NodifyEditor)window.FindName("PhaseEditor")).ItemContainerStyle, Is.SameAs(style));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void SharedContainerStyleSynchronizesLocationInBothDirections()
        {
            EnsureApplication();
            var resources = new MainWindow();
            var host = new Window { Width = 600, Height = 400 };
            try
            {
                var editor = new NodifyEditor
                {
                    ItemContainerStyle = (Style)resources.Resources["GraphItemContainerStyle"],
                    ItemsSource = new ObservableCollection<GraphNodeViewModel>
                    {
                        new GraphNodeViewModel { Id = "layout-test", Location = new Point(120, 80) }
                    }
                };
                host.Content = editor;
                host.Show();
                host.UpdateLayout();

                var container = editor.ItemContainerGenerator.ContainerFromIndex(0) as ItemContainer;
                Assert.That(container, Is.Not.Null);
                Assert.That(container.Location.X, Is.EqualTo(120).Within(0.001));
                Assert.That(container.Location.Y, Is.EqualTo(80).Within(0.001));

                container.Location = new Point(330, 210);
                host.UpdateLayout();
                var node = (GraphNodeViewModel)container.DataContext;
                Assert.That(node.Location.X, Is.EqualTo(330).Within(0.001));
                Assert.That(node.Location.Y, Is.EqualTo(210).Within(0.001));

                node.Location = new Point(475, 125);
                host.UpdateLayout();
                Assert.That(container.Location.X, Is.EqualTo(475).Within(0.001));
                Assert.That(container.Location.Y, Is.EqualTo(125).Within(0.001));
            }
            finally
            {
                host.Close();
                resources.Close();
            }
        }

        [Test]
        public void ActionPickerIsOwnerScopedAndSearchable()
        {
            var sessionChoices = ActionEditorRegistry.PickerChoices(ActionOwnerScope.SessionDecisionOptionSequence).ToList();
            Assert.That(sessionChoices.Any(choice => choice.TypeKey == ActionTypeKeys.SessionGoto), Is.True);
            Assert.That(sessionChoices.Any(choice => choice.TypeKey == ActionTypeKeys.PhaseGoto), Is.False);

            var nestedSessionChoices = ActionEditorRegistry.PickerChoices(ActionOwnerScope.SessionDecisionPromptChoiceSequence).ToList();
            Assert.That(nestedSessionChoices.Any(choice => choice.TypeKey == ActionTypeKeys.SessionGoto), Is.False);
            Assert.That(nestedSessionChoices.Any(choice => choice.TypeKey == ActionTypeKeys.StatIncrease), Is.True);

            var phaseChoices = ActionEditorRegistry.PickerChoices(ActionOwnerScope.ChoiceOptionSequence).ToList();
            Assert.That(phaseChoices.Any(choice => choice.TypeKey == ActionTypeKeys.PhaseGoto), Is.True);
            Assert.That(phaseChoices.Any(choice => choice.TypeKey == ActionTypeKeys.SessionGoto), Is.False);

            var cardChoices = ActionEditorRegistry.PickerChoices(ActionOwnerScope.CardSequence).ToList();
            Assert.That(cardChoices.Any(choice => choice.TypeKey == ActionTypeKeys.PhaseGoto), Is.False);

            var sequence = new ActionSequenceEditorViewModel(
                new GraphNodeViewModel { Id = "action-test" }, "sequence-test",
                ActionOwnerScope.PhaseActionSequence, null, null, null,
                new[] { new ExitOption { Id = "", Name = "Unassigned" } });
            sequence.SearchText = "temperature";
            Assert.That(sequence.ActionTypePickerView.Cast<ActionTypeChoice>().All(choice =>
                choice.SearchText.IndexOf("temperature", System.StringComparison.OrdinalIgnoreCase) >= 0), Is.True);

            var prompt = sequence.CreateDefaultInstance(ActionTypeKeys.PromptChoice, "prompt-test");
            Assert.That(prompt, Is.TypeOf<PromptChoiceInstanceDefinition>());
            Assert.That(((PromptChoiceInstanceDefinition)prompt).Options, Has.Count.EqualTo(2));

            var sessionSequence = new ActionSequenceEditorViewModel(
                new GraphNodeViewModel { Id = "session-decision" }, "session-sequence",
                ActionOwnerScope.SessionDecisionOptionSequence,
                new[] { prompt }, null, null, null);
            Assert.That(sessionSequence.Rows[0].PromptOptions[0].ActionSequence.OwnerScope,
                Is.EqualTo(ActionOwnerScope.SessionDecisionPromptChoiceSequence));
        }

        [Test]
        public void PromptChoiceNestedSequenceTemplateRenders()
        {
            EnsureApplication();
            var resources = new MainWindow();
            var host = new Window { Width = 800, Height = 600 };
            try
            {
                var card = new CardDefinition
                {
                    Id = "card-prompt",
                    Title = "Prompt Card",
                    Sequence = new ActionSequenceDefinition { Id = "seq-prompt" },
                };
                var prompt = new PromptChoiceInstanceDefinition
                {
                    Id = "prompt-choice",
                    Prompt = "Choose",
                    IsBlocking = true,
                };
                prompt.Options.Add(new PromptChoiceOptionDefinition
                {
                    Id = "prompt-option-a",
                    Label = "A",
                    Sequence = new ActionSequenceDefinition { Id = "prompt-seq-a" },
                });
                prompt.Options.Add(new PromptChoiceOptionDefinition
                {
                    Id = "prompt-option-b",
                    Label = "B",
                    Sequence = new ActionSequenceDefinition { Id = "prompt-seq-b" },
                });
                card.Sequence.Instances.Add(prompt);

                var content = new GameContentDefinition();
                var sequence = CardEditorSequenceHost.Build(card, content);
                var template = (DataTemplate)resources.Resources["ActionSequenceTemplate"];
                host.Resources["ActionSequenceTemplate"] = template;
                host.Content = new ContentControl
                {
                    Content = sequence,
                    ContentTemplate = template,
                };
                host.Show();
                host.UpdateLayout();

                Assert.That(sequence.Rows, Has.Count.EqualTo(1));
                Assert.That(sequence.Rows[0].PromptOptions, Has.Count.EqualTo(2));
                Assert.That(sequence.Rows[0].PromptOptions[0].ActionSequence, Is.Not.Null);
            }
            finally
            {
                host.Close();
                resources.Close();
            }
        }

        [Test]
        public void AutoLayoutRepairsExactDuplicateSavedCoordinates()
        {
            var nodes = new GraphNodeDefinition[]
            {
                new PhaseEntryNodeDefinition { Id = "entry" },
                new ReturnNodeDefinition { Id = "return-a" },
                new ReturnNodeDefinition { Id = "return-b" },
            };
            var saved = new Dictionary<string, (double X, double Y)>
            {
                ["return-a"] = (240, 240),
                ["return-b"] = (240, 240),
            };

            var filled = GraphAutoLayout.FillMissing(nodes, null, saved);

            Assert.That(filled.Values.Distinct().Count(), Is.EqualTo(3));
            Assert.That(filled["return-b"], Is.Not.EqualTo((240d, 240d)));
        }

        [Test]
        public void RelationPickerPersistsStableIdsWhenTitlesDuplicate()
        {
            var picker = new RelationPickerControl();
            picker.SetItems(new[]
            {
                new RelationChoice { Id = "tag-a", DisplayName = "Same title" },
                new RelationChoice { Id = "tag-b", DisplayName = "Same title" },
            }, new[] { "tag-b" });

            CollectionAssert.AreEqual(new[] { "tag-b" }, picker.SelectedIds);
        }

        [Test]
        public void TypedPhaseGotoRowShowsDangerUntilExitIsAssigned()
        {
            var sequence = new ActionSequenceEditorViewModel(
                new GraphNodeViewModel { Id = "action-test" }, "sequence-test",
                ActionOwnerScope.PhaseActionSequence,
                new[] { new PhaseGotoInstanceDefinition { Id = "goto-test", PhaseExitId = "" } },
                null, null,
                new[] { new ExitOption { Id = "", Name = "Unassigned" }, new ExitOption { Id = "exit-a", Name = "Success" } });
            var row = sequence.Rows.Single();

            Assert.That(row.HasChoiceEditor, Is.True);
            Assert.That(row.IsDanger, Is.True);
            row.TextValue = "exit-a";
            Assert.That(row.IsDanger, Is.False);
            Assert.That(((PhaseGotoInstanceDefinition)row.Definition).PhaseExitId, Is.EqualTo("exit-a"));
        }

        [Test]
        public void LibraryModeTabsStayOutsideBothLibraryDrawers()
        {
            EnsureApplication();
            var window = new MainWindow();
            try
            {
                var tabs = (FrameworkElement)window.FindName("LibraryModeTabs");
                var sessionPanel = (FrameworkElement)window.FindName("SessionLibraryPanel");
                var phasePanel = (FrameworkElement)window.FindName("PhaseLibraryPanel");
                var sessionsTab = (FrameworkElement)window.FindName("SessionLibraryTabButton");
                var phasesTab = (FrameworkElement)window.FindName("PhaseLibraryTabButton");

                Assert.That(Grid.GetRow(tabs), Is.EqualTo(0));
                Assert.That(Grid.GetRow(sessionPanel), Is.EqualTo(1));
                Assert.That(Grid.GetRow(phasePanel), Is.EqualTo(1));
                Assert.That(VisualTreeHelper.GetParent(sessionsTab), Is.SameAs(tabs));
                Assert.That(VisualTreeHelper.GetParent(phasesTab), Is.SameAs(tabs));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void CatalogSearch_IsInsideCatalogDrawer_NotHiddenSessionDrawer()
        {
            EnsureApplication();
            var window = new MainWindow();
            try
            {
                var search = (FrameworkElement)window.FindName("CatalogSearchBox");
                var catalogs = (FrameworkElement)window.FindName("CatalogsLibraryPanel");
                var sessions = (FrameworkElement)window.FindName("SessionLibraryPanel");

                Assert.That(IsVisualDescendant(search, catalogs), Is.True,
                    "Catalog search must be hosted by the visible Catalogs drawer.");
                Assert.That(IsVisualDescendant(search, sessions), Is.False,
                    "Catalog search must not be hidden with the Sessions drawer.");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void LibraryReload_PreservesCardsAndCatalogsDrawer()
        {
            EnsureApplication();
            var window = new MainWindow();
            try
            {
                window.Show();
                var bindLibrary = typeof(MainWindow).GetMethod("BindLibrary",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(bindLibrary, Is.Not.Null);

                foreach (var pair in new[]
                {
                    (Button: "CardsLibraryTabButton", Panel: "CardsLibraryPanel"),
                    (Button: "CatalogsLibraryTabButton", Panel: "CatalogsLibraryPanel"),
                })
                {
                    ((Button)window.FindName(pair.Button)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    bindLibrary.Invoke(window, null);

                    Assert.That(((FrameworkElement)window.FindName(pair.Panel)).Visibility,
                        Is.EqualTo(Visibility.Visible), pair.Panel + " should survive a content reload.");
                    Assert.That(((FrameworkElement)window.FindName("SessionLibraryPanel")).Visibility,
                        Is.EqualTo(Visibility.Collapsed));
                }
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void CatalogLists_FillRemainingWidthInTwoStarColumns()
        {
            EnsureApplication();
            var window = new MainWindow();
            try
            {
                var lists = (Grid)window.FindName("CatalogListsPanel");
                var editor = (FrameworkElement)window.FindName("CatalogEditorPanel");
                var parent = (DockPanel)VisualTreeHelper.GetParent(lists);

                Assert.That(parent.Children[parent.Children.Count - 1], Is.SameAs(lists),
                    "The catalog lists must be the DockPanel fill child.");
                Assert.That(DockPanel.GetDock(editor), Is.EqualTo(Dock.Bottom));
                Assert.That(lists.ColumnDefinitions.Count, Is.EqualTo(2));
                Assert.That(lists.ColumnDefinitions.All(column => column.Width.IsStar), Is.True);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void EveryDeletableLibraryList_HasAnItemDeleteContextMenu()
        {
            EnsureApplication();
            var window = new MainWindow();
            try
            {
                foreach (var pair in new[]
                {
                    (List: "SessionList", Header: "Delete Session"),
                    (List: "PhaseList", Header: "Delete Phase"),
                    (List: "CardList", Header: "Delete Card"),
                    (List: "CatalogEntryList", Header: "Delete Catalog Entry"),
                })
                {
                    var list = (ListBox)window.FindName(pair.List);
                    var menu = list.ContextMenu;
                    Assert.That(menu, Is.Not.Null);
                    Assert.That(((MenuItem)menu.Items[0]).Header, Is.EqualTo(pair.Header));
                }
            }
            finally
            {
                window.Close();
            }
        }

        private static bool IsVisualDescendant(DependencyObject child, DependencyObject ancestor)
        {
            for (var current = VisualTreeHelper.GetParent(child); current != null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, ancestor)) return true;
            }
            return false;
        }
    }
}
