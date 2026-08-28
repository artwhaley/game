using System.Windows;
using System.Windows.Controls;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Explicit choice before editing ports on a shared phase.</summary>
    internal sealed class SharedPhaseTopologyDialog : Window
    {
        public SharedPhaseTopologyDialog(int sessions, int placements)
        {
            Title = "Shared Phase Port Lock";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = "This Phase is shared by " + sessions + " session" + (sessions == 1 ? "" : "s") +
                    " and " + placements + " placement" + (placements == 1 ? "" : "s") + ".",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
            });
            root.Children.Add(new TextBlock
            {
                Text = "Changing its exits could break existing Session graphs. Make Unique will " +
                    "detach the selected placement and apply this port edit to its private clone.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var makeUnique = new Button
            {
                Content = "Make Unique",
                MinWidth = 105,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(8, 3, 8, 3),
            };
            makeUnique.Click += (_, _) => { DialogResult = true; };
            var cancel = new Button
            {
                Content = "Cancel",
                MinWidth = 80,
                Padding = new Thickness(8, 3, 8, 3),
            };
            cancel.Click += (_, _) => { DialogResult = false; };
            buttons.Children.Add(makeUnique);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }
    }
}
