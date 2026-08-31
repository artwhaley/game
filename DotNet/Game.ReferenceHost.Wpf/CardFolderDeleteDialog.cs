using System.Windows;
using System.Windows.Controls;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Choice dialog for deleting a non-empty card folder: cascade the
    /// cards or relocate them to UNASSIGNED. Three-way outcome (cascade / keep
    /// cards / cancel) — never a silent default.</summary>
    internal sealed class CardFolderDeleteDialog : Window
    {
        /// <summary>True when the user chose to delete contained cards with the
        /// folder; false when cards move to UNASSIGNED. Null when canceled.</summary>
        public bool? DeleteCards { get; private set; }

        public CardFolderDeleteDialog(string folderName, int subfolderCount, int cardCount)
        {
            Title = "Delete Card Folder";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var root = new StackPanel { Margin = new Thickness(18) };
            var isBatch = folderName == null;
            root.Children.Add(new TextBlock
            {
                Text = isBatch
                    ? "Delete the selected cards and folders?"
                    : "Folder \"" + folderName + "\" is not empty.",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
            });
            if (isBatch)
            {
                root.Children.Add(new TextBlock
                {
                    Text = "The selection covers " + subfolderCount + " folder" + (subfolderCount == 1 ? "" : "s")
                        + " containing " + cardCount + " card" + (cardCount == 1 ? "" : "s") + " in total.",
                    Margin = new Thickness(0, 0, 0, 14),
                });
            }
            else
            {
                root.Children.Add(new TextBlock
                {
                    Text = "It contains " + subfolderCount + " subfolder" + (subfolderCount == 1 ? "" : "s")
                        + " and " + cardCount + " card" + (cardCount == 1 ? "" : "s") + ".",
                    Margin = new Thickness(0, 0, 0, 14),
                });
            }

            var cascade = new Button
            {
                Content = "Delete Folder and Cards",
                MinWidth = 170,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(8, 3, 8, 3),
                ToolTip = "Removes the folder, all subfolders, and every card inside it (undoable).",
            };
            cascade.Click += (_, _) => { DeleteCards = true; DialogResult = true; };
            var keep = new Button
            {
                Content = "Delete Folder, Keep Cards",
                MinWidth = 160,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(8, 3, 8, 3),
                ToolTip = "Removes the folder tree; contained cards move to UNASSIGNED (undoable).",
            };
            keep.Click += (_, _) => { DeleteCards = false; DialogResult = true; };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(8, 3, 8, 3) };
            cancel.Click += (_, _) => { DialogResult = false; };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cascade);
            buttons.Children.Add(keep);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;

            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false;
            };
        }
    }
}
