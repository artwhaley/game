using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using Nodify;
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
    }
}
