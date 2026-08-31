using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    public partial class MainWindow
    {
        private readonly ObservableCollection<ActionBlockBrowserItem> _actionBlockBrowserItems =
            new ObservableCollection<ActionBlockBrowserItem>();
        private Point _actionBlockDragStart;
        private bool _actionBlockBrowserInitialized;
        private string _pendingActionSelectionSequenceId;
        private List<string> _pendingActionSelectionIds;

        private void OnShowBlocksBrowser(object sender, RoutedEventArgs e)
        {
            ActionBrowserContent.Visibility = Visibility.Collapsed;
            BlocksBrowserContent.Visibility = Visibility.Visible;
            ResourceBrowserContent.Visibility = Visibility.Collapsed;
            if (!_actionBlockBrowserInitialized)
            {
                BlocksBrowserList.ItemsSource = _actionBlockBrowserItems;
                _actionBlockBrowserInitialized = true;
            }
            RefreshActionBlockBrowser();
        }

        private void OnActionBlockSearchChanged(object sender, TextChangedEventArgs e) => RefreshActionBlockBrowser();

        private void RefreshActionBlockBrowser()
        {
            if (!_actionBlockBrowserInitialized) return;
            var selectedId = (BlocksBrowserList.SelectedItem as ActionBlockBrowserItem)?.Id;
            var query = (BlocksSearchBox.Text ?? "").Trim();
            var blocks = WithConnectionResult(connection => ActionBlockRepository.List(connection, query))
                ?? new List<ActionBlockDefinition>();
            _actionBlockBrowserItems.Clear();
            foreach (var block in blocks) _actionBlockBrowserItems.Add(new ActionBlockBrowserItem(block));
            if (selectedId != null) BlocksBrowserList.SelectedItem = _actionBlockBrowserItems.FirstOrDefault(item => item.Id == selectedId);
            if (BlocksBrowserList.SelectedItem == null && _actionBlockBrowserItems.Count > 0) BlocksBrowserList.SelectedIndex = 0;
        }

        private void OnActionBlockBrowserMouseDown(object sender, MouseButtonEventArgs e)
        {
            _actionBlockDragStart = e.GetPosition(BlocksBrowserList);
        }

        private void OnActionBlockBrowserMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !(BlocksBrowserList.SelectedItem is ActionBlockBrowserItem item)) return;
            var point = e.GetPosition(BlocksBrowserList);
            if (Math.Abs(point.X - _actionBlockDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _actionBlockDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (_actionDragInProgress) return;
            _actionDragInProgress = true;
            try { DragDrop.DoDragDrop(BlocksBrowserList, new DataObject(typeof(ActionBlockBrowserItem), item), DragDropEffects.Copy); }
            finally { _actionDragInProgress = false; }
        }

        private void OnActionBlockRightClick(object sender, MouseButtonEventArgs e)
        {
            if (!(e.OriginalSource is DependencyObject source)) return;
            var item = ItemsControl.ContainerFromElement(BlocksBrowserList, source) as ListBoxItem;
            if (item == null) return;
            item.IsSelected = true; item.Focus();
        }

        private void OnActionBlockContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (!(BlocksBrowserList.SelectedItem is ActionBlockBrowserItem)) e.Handled = true;
        }

        private void OnSaveSelectedActionBlock(object sender, RoutedEventArgs e)
        {
            var sequence = (sender as FrameworkElement)?.DataContext as ActionSequenceEditorViewModel;
            if (sequence == null) return;
            var selected = sequence.SelectedRows.ToList();
            if (selected.Count == 0) { StatusText.Text = "Select one or more Actions first."; return; }
            SaveActionBlock(sequence, selected.Select(row => row.Definition));
        }

        private void OnSaveEntireActionBlock(object sender, RoutedEventArgs e)
        {
            var sequence = (sender as FrameworkElement)?.DataContext as ActionSequenceEditorViewModel;
            if (sequence == null) return;
            SaveActionBlock(sequence, sequence.Rows.Select(row => row.Definition));
        }

        private void SaveActionBlock(ActionSequenceEditorViewModel sequence, IEnumerable<ActionInstanceDefinition> actions)
        {
            var name = PromptForTitle("New Action Block", "Name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            var block = new ActionBlockDefinition
            {
                Id = StableIds.New(), Name = name.Trim(), FormatVersion = ActionBlockSerializer.CurrentFormatVersion,
                TemplateJson = ActionBlockSerializer.Serialize(actions, sequence.SourcePhaseId)
            };
            PushOrMergeWithReload(new CreateActionBlockCommand(OpenConnection, block), () =>
            {
                RefreshActionBlockBrowser();
                StatusText.Text = "Saved Action Block '" + block.Name + "'.";
            });
        }

        private void OnRenameActionBlock(object sender, RoutedEventArgs e)
        {
            var item = BlocksBrowserList.SelectedItem as ActionBlockBrowserItem;
            if (item == null) return;
            var name = PromptForLibraryRename("Action Block", item.Name);
            if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
            PushOrMergeWithReload(new RenameActionBlockCommand(OpenConnection, item.Id, item.Name, name), () =>
            {
                RefreshActionBlockBrowser();
                StatusText.Text = "Renamed Action Block to '" + name + "'.";
            });
        }

        private void OnDeleteActionBlock(object sender, RoutedEventArgs e)
        {
            var item = BlocksBrowserList.SelectedItem as ActionBlockBrowserItem;
            if (item == null) return;
            if (MessageBox.Show(this, "Delete Action Block '" + item.Name + "'? Existing inserted Actions are unchanged.",
                "Action Block", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            PushOrMergeWithReload(new DeleteActionBlockCommand(OpenConnection, item.Id), () =>
            {
                RefreshActionBlockBrowser();
                StatusText.Text = "Deleted Action Block '" + item.Name + "'.";
            });
        }

        private void OnInsertActionBlock(object sender, RoutedEventArgs e)
        {
            var item = BlocksBrowserList.SelectedItem as ActionBlockBrowserItem;
            var sequence = (sender as FrameworkElement)?.DataContext as ActionSequenceEditorViewModel;
            if (item != null && sequence != null) InsertActionBlock(item.Block, sequence, sequence.Rows.Count);
        }

        private void OnActionBlockDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(typeof(ActionBlockBrowserItem)) is ActionBlockBrowserItem item)) return;
            var target = (sender as FrameworkElement)?.DataContext as ActionSequenceEditorViewModel;
            if (target == null && (sender as FrameworkElement)?.DataContext is ActionRowData row) target = row.Sequence;
            if (target == null) return;
            var index = target.Rows.Count;
            if ((sender as FrameworkElement)?.DataContext is ActionRowData targetRow) index = target.Rows.IndexOf(targetRow);
            InsertActionBlock(item.Block, target, index);
            e.Handled = true;
        }

        private void OnActionBlockDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(ActionBlockBrowserItem)) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void InsertActionBlock(ActionBlockDefinition block, ActionSequenceEditorViewModel target, int targetIndex)
        {
            var destination = new ActionBlockDestination
            {
                Scope = target.OwnerScope, PhaseId = target.SourcePhaseId,
                SessionDecisionNodeId = target.IsSessionDecisionOption ? target.OwnerNode?.Id : null,
                SessionDecisionOptionId = target.IsSessionDecisionOption ? target.OptionId : null
            };
            var prefix = (target.IdentityPrefix ?? "sequence") + "-block-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var result = ActionBlockInsertionService.ValidateAndClone(block, destination, _vm.Content, prefix);
            if (!result.IsValid)
            {
                StatusText.Text = "Action Block rejected: " + string.Join(" ", result.Errors);
                return;
            }
            targetIndex = Math.Max(0, Math.Min(targetIndex, target.Rows.Count));
            var before = SequenceSnapshotUtility.Clone(target);
            var after = SequenceSnapshotUtility.Clone(target);
            after.Instances.InsertRange(targetIndex, result.ClonedActions);
            _pendingActionSelectionSequenceId = target.SequenceId;
            _pendingActionSelectionIds = result.ClonedActions.Select(action => action.Id).ToList();
            if (target.OwnerScope == ActionOwnerScope.CardSequence)
            {
                var bufferSequence = _cardBuffer?.FindSequence(target.SequenceId) ?? _cardBuffer?.Sequence;
                if (bufferSequence == null) return;
                PushCommand(new InMemorySequenceCommand("Insert Action Block",
                    sequence => ReplaceBufferSequence(bufferSequence, sequence), before, after, RebuildCardSequenceHost));
                if (CardActionSequenceHost.Content is ActionSequenceEditorViewModel cardEditor)
                    cardEditor.SelectRowsByIds(_pendingActionSelectionIds);
            }
            else
            {
                PushCommand(new InsertActionBlockCommand(OpenConnection, target.SequenceId,
                    destination.SessionDecisionNodeId, destination.SessionDecisionOptionId, before, after),
                    reloadSession: target.IsSessionDecisionOption, reloadPhase: !target.IsSessionDecisionOption);
            }
            StatusText.Text = "Inserted Action Block '" + block.Name + "'.";
        }

        private static void ReplaceBufferSequence(ActionSequenceDefinition destination, ActionSequenceDefinition source)
        {
            destination.Instances.Clear();
            destination.Instances.AddRange(source.Instances);
        }

        private void ApplyPendingActionSelection(GraphEditorViewModel graph)
        {
            if (graph == null || _pendingActionSelectionIds == null) return;
            foreach (var node in graph.Nodes)
            {
                var sequences = new List<ActionSequenceEditorViewModel>();
                if (node.ActionSequence != null) sequences.Add(node.ActionSequence);
                foreach (var option in node.DecisionRows) if (option.ActionSequence != null) sequences.Add(option.ActionSequence);
                foreach (var sequence in sequences)
                    if (sequence.SequenceId == _pendingActionSelectionSequenceId)
                    {
                        sequence.SelectRowsByIds(_pendingActionSelectionIds);
                        _pendingActionSelectionSequenceId = null;
                        _pendingActionSelectionIds = null;
                        return;
                    }
            }
        }

        private void OnActionRowMouseDown(object sender, MouseButtonEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as ActionRowData;
            if (row == null || row.Sequence == null) return;
            if (FindVisualAncestor<Control>(e.OriginalSource as DependencyObject) is TextBox ||
                FindVisualAncestor<Control>(e.OriginalSource as DependencyObject) is ComboBox ||
                FindVisualAncestor<Control>(e.OriginalSource as DependencyObject) is Button ||
                FindVisualAncestor<RelationPickerControl>(e.OriginalSource as DependencyObject) != null) return;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) row.Sequence.SelectRange(row);
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) row.Sequence.ToggleSelection(row);
            else row.Sequence.SelectOnly(row);
        }

        private static T FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                var result = source as T;
                if (result != null) return result;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private void OnActionRowContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ActionRowData)) e.Handled = true;
        }

        private void OnInsertActionBlockBefore(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as ActionRowData;
            if (row == null) return;
            var item = ChooseActionBlock();
            if (item != null) InsertActionBlock(item.Block, row.Sequence, row.Sequence.Rows.IndexOf(row));
        }

        private void OnInsertActionBlockAfter(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as ActionRowData;
            if (row == null) return;
            var item = ChooseActionBlock();
            if (item != null) InsertActionBlock(item.Block, row.Sequence, row.Sequence.Rows.IndexOf(row) + 1);
        }

        private ActionBlockBrowserItem ChooseActionBlock()
        {
            if (!_actionBlockBrowserInitialized)
            {
                BlocksBrowserList.ItemsSource = _actionBlockBrowserItems;
                _actionBlockBrowserInitialized = true;
            }
            RefreshActionBlockBrowser();
            var selected = BlocksBrowserList.SelectedItem as ActionBlockBrowserItem;
            if (selected != null) return selected;
            if (_actionBlockBrowserItems.Count == 0)
            {
                StatusText.Text = "Create or select an Action Block first.";
                return null;
            }
            if (_actionBlockBrowserItems.Count == 1) return _actionBlockBrowserItems[0];

            var list = new ListBox { ItemsSource = _actionBlockBrowserItems, DisplayMemberPath = "DisplayText", MinWidth = 360, MinHeight = 160 };
            list.Style = FindResource("DarkListBox") as Style;
            list.ItemContainerStyle = FindResource("DarkListBoxItem") as Style;
            var ok = new Button { Content = "Insert", Padding = new Thickness(12, 2, 12, 2), IsDefault = true };
            ok.Style = FindResource("DarkButton") as Style;
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 2, 12, 2), IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
            cancel.Style = FindResource("DarkButton") as Style;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = "Choose an Action Block", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(list); panel.Children.Add(buttons);
            var dialog = new Window { Title = "Insert Action Block", Content = panel, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, Background = FindResource("PanelBrush") as Brush };
            ActionBlockBrowserItem choice = null;
            ok.Click += (_, _) => { choice = list.SelectedItem as ActionBlockBrowserItem; if (choice != null) dialog.DialogResult = true; };
            list.SelectedIndex = 0;
            dialog.ShowDialog();
            return choice;
        }
    }
}
