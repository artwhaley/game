using System.Collections.ObjectModel;
using System.Linq;
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

            var phaseChoices = ActionEditorRegistry.PickerChoices(ActionOwnerScope.ChoiceOptionSequence).ToList();
            Assert.That(phaseChoices.Any(choice => choice.TypeKey == ActionTypeKeys.PhaseGoto), Is.True);
            Assert.That(phaseChoices.Any(choice => choice.TypeKey == ActionTypeKeys.SessionGoto), Is.False);

            var sequence = new ActionSequenceEditorViewModel(
                new GraphNodeViewModel { Id = "action-test" }, "sequence-test",
                ActionOwnerScope.PhaseActionSequence, null, null, null,
                new[] { new ExitOption { Id = "", Name = "Unassigned" } });
            sequence.SearchText = "temperature";
            Assert.That(sequence.ActionTypePickerView.Cast<ActionTypeChoice>().All(choice =>
                choice.SearchText.IndexOf("temperature", System.StringComparison.OrdinalIgnoreCase) >= 0), Is.True);
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
    }
}
