using System.Windows;
using System.Windows.Controls;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Explicit confirmation for a destructive, in-use PhaseExit delete.</summary>
    internal sealed class PhaseExitDeleteDialog : Window
    {
        public PhaseExitDeleteDialog(string exitName, int gotoCount, int edgeCount)
        {
            Title = "Delete Phase Exit";
            Width = 430;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = "Phase Exit \"" + exitName + "\" is still in use.",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
            });
            root.Children.Add(new TextBlock { Text = gotoCount + " GOTO action" + (gotoCount == 1 ? "" : "s") + " reference it." });
            root.Children.Add(new TextBlock { Text = edgeCount + " Session connection" + (edgeCount == 1 ? "" : "s") + " use it." });
            root.Children.Add(new TextBlock
            {
                Text = "Deleting it will set those GOTO actions to Unassigned\nand disconnect the Session connection.",
                Margin = new Thickness(0, 10, 0, 14),
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var delete = new Button { Content = "Delete Anyway", MinWidth = 105, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 3, 8, 3) };
            delete.Click += (_, _) => { DialogResult = true; };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(8, 3, 8, 3) };
            cancel.Click += (_, _) => { DialogResult = false; };
            buttons.Children.Add(delete);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }
    }
}
