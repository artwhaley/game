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
        private readonly ObservableCollection<ActionBlockTreeNode> _actionBlockTreeRoots =
            new ObservableCollection<ActionBlockTreeNode>();
        private readonly HashSet<string> _actionBlockCreatedFolderPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Point _actionBlockDragStart;
        private bool _actionBlockBrowserInitialized;
        private ActionBlockTreeNode _selectedActionBlockTreeNode;
        private string _selectedActionBlockFolderPath = "";
        private string _pendingActionSelectionSequenceId;
        private List<string> _pendingActionSelectionIds;

        private void OnShowBlocksBrowser(object sender, RoutedEventArgs e)
        {
            ActionBrowserContent.Visibility = Visibility.Collapsed;
            BlocksBrowserContent.Visibility = Visibility.Visible;
            ResourceBrowserContent.Visibility = Visibility.Collapsed;
            if (!_actionBlockBrowserInitialized)
            {
                BlocksBrowserTree.ItemsSource = _actionBlockTreeRoots;
                _actionBlockBrowserInitialized = true;
            }
            RefreshActionBlockBrowser();
        }

        private void OnActionBlockSearchChanged(object sender, TextChangedEventArgs e) => RefreshActionBlockBrowser();

        private void RefreshActionBlockBrowser(bool selectFirstWhenEmpty = true)
        {
            if (!_actionBlockBrowserInitialized) return;
            var query = (BlocksSearchBox.Text ?? "").Trim();
            var allBlocks = WithConnectionResult(connection => ActionBlockRepository.List(connection))
                ?? new List<ActionBlockDefinition>();
            var matches = allBlocks.Where(block => string.IsNullOrEmpty(query) ||
                (block.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (block.Id ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                ActionBlockSerializer.NormalizeFolderPath(block.FolderPath).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(block => block.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var root = new ActionBlockTreeNode("", "All Action Blocks", "", isRoot: true);
            var unassigned = new ActionBlockTreeNode("unassigned", "UNASSIGNED", "", isUnassigned: true);
            root.AddChild(unassigned);
            var folders = new Dictionary<string, ActionBlockTreeNode>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _actionBlockCreatedFolderPaths)
                if (string.IsNullOrEmpty(query) || path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    paths.Add(path);
            foreach (var block in matches)
            {
                var path = ActionBlockSerializer.NormalizeFolderPath(block.FolderPath);
                if (path.Length > 0)
                {
                    var current = path;
                    while (current.Length > 0)
                    {
                        paths.Add(current);
                        var separator = current.LastIndexOf('/');
                        current = separator < 0 ? "" : current.Substring(0, separator);
                    }
                }
            }
            foreach (var path in paths.OrderBy(path => path.Count(ch => ch == '/')).ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var separator = path.LastIndexOf('/');
                var parentPath = separator < 0 ? "" : path.Substring(0, separator);
                var parent = parentPath.Length == 0 ? root : folders[parentPath];
                var name = separator < 0 ? path : path.Substring(separator + 1);
                var node = new ActionBlockTreeNode("folder:" + path, name, path);
                parent.AddChild(node);
                folders[path] = node;
            }
            foreach (var block in matches)
            {
                var item = new ActionBlockTreeNode(block.Id, block.Name, block.FolderPath,
                    block: new ActionBlockBrowserItem(block));
                var path = ActionBlockSerializer.NormalizeFolderPath(block.FolderPath);
                (path.Length == 0 ? unassigned : folders[path]).AddChild(item);
            }
            _actionBlockTreeRoots.Clear();
            _actionBlockTreeRoots.Add(root);
            if (_selectedActionBlockTreeNode != null)
                _selectedActionBlockFolderPath = ActionBlockSerializer.NormalizeFolderPath(
                    _selectedActionBlockTreeNode.IsBlock ? _selectedActionBlockTreeNode.Block.Block.FolderPath : _selectedActionBlockTreeNode.Path);
        }

        private void OnActionBlockBrowserMouseDown(object sender, MouseButtonEventArgs e)
        {
            _actionBlockDragStart = e.GetPosition(BlocksBrowserTree);
        }

        private void OnActionBlockBrowserMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _selectedActionBlockTreeNode?.IsBlock != true) return;
            var item = _selectedActionBlockTreeNode.Block;
            var point = e.GetPosition(BlocksBrowserTree);
            if (Math.Abs(point.X - _actionBlockDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _actionBlockDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (_actionDragInProgress) return;
            _actionDragInProgress = true;
            try { DragDrop.DoDragDrop(BlocksBrowserTree, new DataObject(typeof(ActionBlockBrowserItem), item), DragDropEffects.Copy | DragDropEffects.Move); }
            finally { _actionDragInProgress = false; }
        }

        private void OnActionBlockRightClick(object sender, MouseButtonEventArgs e)
        {
            if (!(e.OriginalSource is DependencyObject source)) return;
            var item = FindActionBlockTreeItem(source);
            if (item == null) return;
            item.IsSelected = true; item.Focus();
        }

        private void OnActionBlockContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_selectedActionBlockTreeNode == null) e.Handled = true;
        }

        private void OnActionBlockTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as ActionBlockTreeNode;
            if (node == null) return;
            _selectedActionBlockTreeNode = node;
            _selectedActionBlockFolderPath = ActionBlockSerializer.NormalizeFolderPath(
                node.IsBlock ? node.Block.Block.FolderPath : node.Path);
        }

        private void OnActionBlockTreeDragOver(object sender, DragEventArgs e)
        {
            var target = FindActionBlockTreeNode(e.OriginalSource as DependencyObject);
            e.Effects = target != null && target.IsFolder &&
                e.Data.GetDataPresent(typeof(ActionBlockBrowserItem)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnActionBlockTreeDrop(object sender, DragEventArgs e)
        {
            var source = e.Data.GetData(typeof(ActionBlockBrowserItem)) as ActionBlockBrowserItem;
            var target = FindActionBlockTreeNode(e.OriginalSource as DependencyObject);
            if (source == null || target == null || !target.IsFolder) return;
            var folder = target.IsRoot || target.IsUnassigned ? "" : target.Path;
            var before = ActionBlockSerializer.NormalizeFolderPath(source.Block.FolderPath);
            if (string.Equals(before, folder, StringComparison.OrdinalIgnoreCase)) return;
            PushOrMergeWithReload(new MoveActionBlockFolderCommand(OpenConnection, source.Id, before, folder), () =>
            {
                _selectedActionBlockFolderPath = folder;
                RefreshActionBlockBrowser();
                StatusText.Text = "Moved Action Block '" + source.Name + "' to " +
                    (folder.Length == 0 ? "UNASSIGNED." : "'" + folder + "'.");
            });
            e.Handled = true;
        }

        private static TreeViewItem FindActionBlockTreeItem(DependencyObject source)
        {
            while (source != null)
            {
                var item = source as TreeViewItem;
                if (item != null) return item;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private static ActionBlockTreeNode FindActionBlockTreeNode(DependencyObject source)
        {
            return (FindActionBlockTreeItem(source)?.DataContext as ActionBlockTreeNode);
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
            var actionList = (actions ?? Enumerable.Empty<ActionInstanceDefinition>()).ToList();
            if (actionList.Count == 0)
            {
                StatusText.Text = "An Action Block must contain at least one Action.";
                return;
            }
            var name = PromptForTitle("New Action Block", "Name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            var block = new ActionBlockDefinition
            {
                Id = StableIds.New(), Name = name.Trim(), FormatVersion = ActionBlockSerializer.CurrentFormatVersion,
                FolderPath = ActionBlockSerializer.NormalizeFolderPath(_selectedActionBlockFolderPath),
                TemplateJson = ActionBlockSerializer.Serialize(actionList, sequence.SourcePhaseId, _selectedActionBlockFolderPath)
            };
            PushOrMergeWithReload(new CreateActionBlockCommand(OpenConnection, block), () =>
            {
                RefreshActionBlockBrowser();
                StatusText.Text = "Saved Action Block '" + block.Name + "'.";
            });
        }

        private void OnRenameActionBlock(object sender, RoutedEventArgs e)
        {
            var item = _selectedActionBlockTreeNode?.Block;
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
            var item = _selectedActionBlockTreeNode?.Block;
            if (item == null) return;
            if (MessageBox.Show(this, "Delete Action Block '" + item.Name + "'? Existing inserted Actions are unchanged.",
                "Action Block", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            PushOrMergeWithReload(new DeleteActionBlockCommand(OpenConnection, item.Id), () =>
            {
                RefreshActionBlockBrowser();
                StatusText.Text = "Deleted Action Block '" + item.Name + "'.";
            });
        }

        private void OnNewActionBlockFolder(object sender, RoutedEventArgs e)
        {
            var name = PromptForTitle("New Action Block Folder", "Folder name:");
            if (name == null) return;
            var parent = _selectedActionBlockTreeNode == null || _selectedActionBlockTreeNode.IsRoot ||
                _selectedActionBlockTreeNode.IsUnassigned ? "" :
                ActionBlockSerializer.NormalizeFolderPath(_selectedActionBlockTreeNode.IsBlock
                    ? _selectedActionBlockTreeNode.Block.Block.FolderPath : _selectedActionBlockTreeNode.Path);
            var child = ActionBlockSerializer.NormalizeFolderPath(name);
            var path = child.IndexOf('/') >= 0 || parent.Length == 0 ? child : parent + "/" + child;
            if (path.Length == 0) return;
            _actionBlockCreatedFolderPaths.Add(path);
            _selectedActionBlockFolderPath = path;
            RefreshActionBlockBrowser();
            StatusText.Text = "Action Block folder '" + path + "' is ready. Create or move a block into it.";
        }

        private void OnMoveActionBlockToFolder(object sender, RoutedEventArgs e)
        {
            var item = _selectedActionBlockTreeNode?.Block;
            if (item == null) return;
            var destination = PromptForTitle("Move Action Block", "Folder path (blank for UNASSIGNED):");
            if (destination == null) return;
            var folder = ActionBlockSerializer.NormalizeFolderPath(destination);
            var oldFolder = ActionBlockSerializer.NormalizeFolderPath(item.Block.FolderPath);
            if (folder == oldFolder) return;
            if (folder.Length > 0) _actionBlockCreatedFolderPaths.Add(folder);
            PushOrMergeWithReload(new MoveActionBlockFolderCommand(OpenConnection, item.Id, oldFolder, folder), () =>
            {
                _selectedActionBlockFolderPath = folder;
                RefreshActionBlockBrowser();
                StatusText.Text = "Moved Action Block '" + item.Name + "' to " +
                    (folder.Length == 0 ? "UNASSIGNED." : "'" + folder + "'.");
            });
        }

        private void OnInsertActionBlock(object sender, RoutedEventArgs e)
        {
            var sequence = (sender as FrameworkElement)?.DataContext as ActionSequenceEditorViewModel;
            if (sequence == null) return;
            var item = ChooseActionBlock();
            if (item != null) InsertActionBlock(item.Block, sequence, sequence.Rows.Count);
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
            var prefix = (target.IdentityPrefix ?? "sequence") + "-block-" + StableIds.New();
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
                ActionSequenceEditorTree.Find(CardActionSequenceHost.Content as ActionSequenceEditorViewModel,
                    target.SequenceId)?.SelectRowsByIds(_pendingActionSelectionIds);
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
                var sequence = ActionSequenceEditorTree.Find(node.ActionSequence, _pendingActionSelectionSequenceId);
                if (sequence == null)
                    foreach (var option in node.DecisionRows)
                    {
                        sequence = ActionSequenceEditorTree.Find(option.ActionSequence, _pendingActionSelectionSequenceId);
                        if (sequence != null) break;
                    }
                if (sequence != null)
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
                BlocksBrowserTree.ItemsSource = _actionBlockTreeRoots;
                _actionBlockBrowserInitialized = true;
            }
            RefreshActionBlockBrowser(selectFirstWhenEmpty: false);
            var chooserBlocks = WithConnectionResult(connection => ActionBlockRepository.List(connection))
                ?? new List<ActionBlockDefinition>();
            var candidates = new ObservableCollection<ActionBlockBrowserItem>(
                chooserBlocks.Select(block => new ActionBlockBrowserItem(block)));
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count == 0)
            {
                StatusText.Text = "Create an Action Block first.";
                return null;
            }

            var list = new ListBox { ItemsSource = candidates, DisplayMemberPath = "DisplayText", MinWidth = 360, MinHeight = 160 };
            list.Style = FindResource("DarkListBox") as Style;
            list.ItemContainerStyle = FindResource("DarkListBoxItem") as Style;
            var search = new TextBox { MinWidth = 360, Margin = new Thickness(0, 0, 0, 5) };
            search.Style = FindResource("DarkTextBox") as Style;
            search.ToolTip = "Search Action Blocks by name";
            var ok = new Button { Content = "Insert", Padding = new Thickness(12, 2, 12, 2), IsDefault = true };
            ok.Style = FindResource("DarkButton") as Style;
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 2, 12, 2), IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
            cancel.Style = FindResource("DarkButton") as Style;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = "Choose an Action Block", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(search);
            panel.Children.Add(list); panel.Children.Add(buttons);
            var dialog = new Window { Title = "Insert Action Block", Content = panel, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, Background = FindResource("PanelBrush") as Brush };
            ActionBlockBrowserItem choice = null;
            Action refresh = () =>
            {
                var query = (search.Text ?? "").Trim();
                var visible = candidates.Where(item => item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                list.ItemsSource = visible;
                ok.IsEnabled = visible.Count > 0;
                if (visible.Count > 0) list.SelectedIndex = 0;
            };
            Action commit = () =>
            {
                choice = list.SelectedItem as ActionBlockBrowserItem;
                if (choice != null) dialog.DialogResult = true;
            };
            search.TextChanged += (_, _) => refresh();
            search.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) { commit(); args.Handled = true; }
            };
            list.MouseDoubleClick += (_, _) => commit();
            ok.Click += (_, _) => commit();
            list.SelectedIndex = 0;
            refresh();
            dialog.ShowDialog();
            return choice;
        }
    }
}
