using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Final pre-Milestone-C authoring surfaces: the resource browser used by
    /// Cutscene and Toy Pattern rows. Resources are stable-ID catalog entries;
    /// the browser only changes identity/name and never edits host bindings.
    /// </summary>
    public partial class MainWindow
    {
        private readonly ObservableCollection<ResourceDefinition> _resourceBrowserItems =
            new ObservableCollection<ResourceDefinition>();
        private Point _resourceDragStart;
        private bool _resourceBrowserInitialized;

        private void OnShowActionBrowser(object sender, RoutedEventArgs e)
        {
            ActionBrowserContent.Visibility = Visibility.Visible;
            ResourceBrowserContent.Visibility = Visibility.Collapsed;
            RefreshActionBrowser();
        }

        private void OnShowResourceBrowser(object sender, RoutedEventArgs e)
        {
            ActionBrowserContent.Visibility = Visibility.Collapsed;
            ResourceBrowserContent.Visibility = Visibility.Visible;
            if (!_resourceBrowserInitialized)
            {
                ResourceKindFilter.ItemsSource = new[] { "(All kinds)", ResourceKinds.Cutscene, ResourceKinds.ToyPattern };
                ResourceKindFilter.SelectedIndex = 0;
                ResourceBrowserList.ItemsSource = _resourceBrowserItems;
                _resourceBrowserInitialized = true;
            }
            RefreshResourceBrowser();
        }

        private void OnResourceBrowserSearchChanged(object sender, TextChangedEventArgs e) => RefreshResourceBrowser();

        private void OnResourceKindFilterChanged(object sender, SelectionChangedEventArgs e) => RefreshResourceBrowser();

        private void RefreshResourceBrowser()
        {
            if (!_resourceBrowserInitialized || _vm?.Content == null) return;
            var selectedId = (ResourceBrowserList.SelectedItem as ResourceDefinition)?.Id;
            var query = (ResourceBrowserSearchBox.Text ?? "").Trim();
            var selectedKind = ResourceKindFilter.SelectedItem as string;
            _resourceBrowserItems.Clear();
            foreach (var resource in _vm.Content.Resources
                .Where(resource => selectedKind == "(All kinds)" || string.IsNullOrEmpty(selectedKind) ||
                    string.Equals(resource.Kind, selectedKind, StringComparison.OrdinalIgnoreCase))
                .Where(resource => string.IsNullOrEmpty(query) ||
                    (resource.Id ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (resource.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase))
            {
                _resourceBrowserItems.Add(resource);
            }
            if (selectedId != null)
                ResourceBrowserList.SelectedItem = _resourceBrowserItems.FirstOrDefault(resource => resource.Id == selectedId);
            if (ResourceBrowserList.SelectedItem == null && _resourceBrowserItems.Count > 0)
                ResourceBrowserList.SelectedIndex = 0;
        }

        private void OnNewResource(object sender, RoutedEventArgs e)
        {
            var kind = ResourceKindFilter.SelectedItem as string;
            if (string.IsNullOrEmpty(kind) || kind == "(All kinds)")
            {
                MessageBox.Show(this, "Choose a concrete resource kind before creating a resource.",
                    "Resource", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var name = PromptForTitle("New Resource", "Name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            var resource = new ResourceDefinition { Id = StableIds.New(), Kind = kind, Name = name };
            PushOrMergeWithReload(new CreateResourceCommand(OpenConnection, resource), () =>
            {
                _vm.Content.Resources.Add(resource);
                RefreshResourceBrowser();
                ResourceBrowserList.SelectedItem = resource;
                RefreshActionAuthoringCatalogs();
                StatusText.Text = $"Created resource '{name}'.";
            });
        }

        private void OnRenameResource(object sender, RoutedEventArgs e)
        {
            if (!(ResourceBrowserList.SelectedItem is ResourceDefinition resource)) return;
            var name = PromptForLibraryRename("Resource", resource.Name);
            if (name == null || string.Equals(name, resource.Name, StringComparison.Ordinal)) return;
            PushOrMergeWithReload(new RenameResourceCommand(OpenConnection, resource.Id, resource.Name, name), () =>
            {
                resource.Name = name;
                RefreshResourceBrowser();
                ResourceBrowserList.SelectedItem = resource;
                RefreshActionAuthoringCatalogs();
                StatusText.Text = $"Renamed resource to '{name}'.";
            });
        }

        private void OnDeleteResource(object sender, RoutedEventArgs e)
        {
            if (!(ResourceBrowserList.SelectedItem is ResourceDefinition resource)) return;
            var unsavedMessage = UnsavedCatalogReferenceMessage(CatalogKinds.Resource, resource.Id);
            if (unsavedMessage != null)
            {
                MessageBox.Show(this, unsavedMessage, "Unsaved Card", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var usage = WithConnectionResult(connection => ResourceRepository.GetUsage(connection, resource.Id).TotalReferences);
            if (usage > 0)
            {
                MessageBox.Show(this,
                    $"'{resource.Name}' is referenced by {usage} action(s) and cannot be deleted.\n\nRemove the action references first.",
                    "Resource", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this, $"Delete resource '{resource.Name}'?", "Resource",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            PushOrMergeWithReload(new DeleteResourceCommand(OpenConnection, resource), () =>
            {
                _vm.Content.Resources.RemoveAll(item => item.Id == resource.Id);
                RefreshResourceBrowser();
                RefreshActionAuthoringCatalogs();
                StatusText.Text = $"Deleted resource '{resource.Name}'.";
            });
        }

        private void OnResourceRightClick(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is ListBox list) || !(e.OriginalSource is DependencyObject source)) return;
            var item = ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
            if (item == null) return;
            item.IsSelected = true;
            item.Focus();
        }

        private void OnResourceContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (!(sender is ListBox list) || !(Mouse.DirectlyOver is DependencyObject source) ||
                !(ItemsControl.ContainerFromElement(list, source) is ListBoxItem))
                e.Handled = true;
        }

        private void OnResourceDragStart(object sender, MouseButtonEventArgs e)
        {
            _resourceDragStart = e.GetPosition(sender as IInputElement);
        }

        private void OnResourceBrowserMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !(ResourceBrowserList.SelectedItem is ResourceDefinition resource)) return;
            var point = e.GetPosition(ResourceBrowserList);
            if (Math.Abs(point.X - _resourceDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _resourceDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (_actionDragInProgress) return;
            _actionDragInProgress = true;
            try
            {
                DragDrop.DoDragDrop(ResourceBrowserList,
                    new DataObject(typeof(ResourceDefinition), resource), DragDropEffects.Copy);
            }
            finally
            {
                _actionDragInProgress = false;
            }
        }
    }
}
