using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nodify;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    public partial class MainWindow
    {
        private PortalEndpointViewModel _portalDragEndpoint;
        private Point _portalDragOriginal;
        private Point _portalDragOffset;
        private bool _portalDragActive;

        private GraphEditorViewModel EditorFor(PortalEndpointViewModel endpoint)
            => endpoint?.GraphKind == "session" ? (GraphEditorViewModel)_vm.SessionGraph : _vm.PhaseGraph;

        private NodifyEditor NodifyEditorFor(PortalEndpointViewModel endpoint)
            => endpoint?.GraphKind == "session" ? SessionEditor : PhaseEditor;

        private void OnGraphConnectionContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var menu = (sender as FrameworkElement)?.ContextMenu;
            var connection = (sender as FrameworkElement)?.DataContext as ConnectionViewModel;
            if (menu == null || connection == null) return;
            if (menu.Items.Count > 0 && menu.Items[0] is MenuItem insert)
                insert.IsEnabled = !string.IsNullOrEmpty(connection.EdgeId) && connection.PortalPair == null;
            if (menu.Items.Count > 1 && menu.Items[1] is MenuItem remove)
                remove.IsEnabled = connection.PortalPair != null;
        }

        private void OnInsertBridgePairFromMenu(object sender, RoutedEventArgs e)
        {
            var menu = (sender as MenuItem)?.Parent as ContextMenu;
            var connection = (menu?.PlacementTarget as FrameworkElement)?.DataContext as ConnectionViewModel;
            if (connection == null) connection = (sender as FrameworkElement)?.DataContext as ConnectionViewModel;
            if (connection == null || string.IsNullOrEmpty(connection.EdgeId) || connection.PortalPair != null) return;
            InsertBridgePair(connection);
        }

        private void OnInsertPortalPairButton(object sender, RoutedEventArgs e)
        {
            var graphKind = ((FrameworkElement)sender).Tag as string;
            var connection = ChoosePortalConnection(graphKind);
            if (connection != null) InsertBridgePair(connection);
        }

        private ConnectionViewModel ChoosePortalConnection(string graphKind)
        {
            var graph = string.Equals(graphKind, "session", StringComparison.OrdinalIgnoreCase)
                ? (GraphEditorViewModel)_vm.SessionGraph : _vm.PhaseGraph;
            var candidates = graph.Connections
                .Where(connection => !string.IsNullOrEmpty(connection.EdgeId) && connection.PortalPair == null)
                .OrderBy(connection => connection.Source?.Owner?.Title ?? connection.Source?.Owner?.Id ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.Target?.Owner?.Title ?? connection.Target?.Owner?.Id ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
            {
                StatusText.Text = "Connect an unportalized edge first.";
                return null;
            }
            if (candidates.Count == 1) return candidates[0];

            var choices = candidates.Select(connection => new PortalEdgeChoice(connection)).ToList();
            var list = new ListBox
            {
                ItemsSource = choices,
                DisplayMemberPath = nameof(PortalEdgeChoice.DisplayText),
                MinWidth = 480,
                MinHeight = 180,
            };
            list.Style = FindResource("DarkListBox") as Style;
            list.ItemContainerStyle = FindResource("DarkListBoxItem") as Style;
            var search = new TextBox { MinWidth = 480, Margin = new Thickness(0, 0, 0, 5) };
            search.Style = FindResource("DarkTextBox") as Style;
            search.ToolTip = "Filter graph edges by node name or edge id";
            var insert = new Button { Content = "Insert", Padding = new Thickness(12, 2, 12, 2), IsDefault = true };
            insert.Style = FindResource("DarkButton") as Style;
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 2, 12, 2), IsCancel = true,
                Margin = new Thickness(6, 0, 0, 0) };
            cancel.Style = FindResource("DarkButton") as Style;
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            buttons.Children.Add(insert);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock
            {
                Text = "Choose the edge for the portal pair",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5),
            });
            panel.Children.Add(search);
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            var dialog = new Window
            {
                Title = "Insert Portal Pair",
                Content = panel,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false,
                Background = FindResource("PanelBrush") as Brush,
            };
            ConnectionViewModel choice = null;
            Action refresh = () =>
            {
                var query = (search.Text ?? "").Trim();
                var visible = choices.Where(item => item.DisplayText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                list.ItemsSource = visible;
                insert.IsEnabled = visible.Count > 0;
                if (visible.Count > 0) list.SelectedIndex = 0;
            };
            Action commit = () =>
            {
                choice = (list.SelectedItem as PortalEdgeChoice)?.Connection;
                if (choice != null) dialog.DialogResult = true;
            };
            search.TextChanged += (_, _) => refresh();
            search.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) { commit(); args.Handled = true; }
            };
            list.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) { commit(); args.Handled = true; }
            };
            list.MouseDoubleClick += (_, _) => commit();
            insert.Click += (_, _) => commit();
            list.SelectedIndex = 0;
            refresh();
            dialog.ShowDialog();
            return choice;
        }

        private sealed class PortalEdgeChoice
        {
            public PortalEdgeChoice(ConnectionViewModel connection)
            {
                Connection = connection;
                var source = connection.Source?.Owner?.Title;
                var target = connection.Target?.Owner?.Title;
                DisplayText = (string.IsNullOrEmpty(source) ? connection.Source?.Owner?.Id : source) +
                    "  →  " + (string.IsNullOrEmpty(target) ? connection.Target?.Owner?.Id : target) +
                    "   [" + connection.EdgeId + "]";
            }

            public ConnectionViewModel Connection { get; }
            public string DisplayText { get; }
        }

        private void InsertBridgePair(ConnectionViewModel connection)
        {
            var editor = connection.GraphKind == "session" ? (GraphEditorViewModel)_vm.SessionGraph : _vm.PhaseGraph;
            var source = connection.Source?.Anchor ?? new Point();
            var target = connection.Target?.Anchor ?? new Point();
            if ((source.X == 0 && source.Y == 0) || (target.X == 0 && target.Y == 0))
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => InsertBridgePair(connection)));
                return;
            }

            var label = NextPortalLabel(editor);
            var numeric = int.Parse(label.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
            var pair = new GraphPortalPairDefinition
            {
                Id = StableIds.New(),
                GraphId = editor.GraphOwnerId,
                EdgeId = connection.EdgeId,
                Label = label,
                ColorSlot = (numeric - 1) % 8,
                SourceX = source.X + (target.X - source.X) * 0.25 - 32,
                SourceY = source.Y + (target.Y - source.Y) * 0.25 - 15,
                TargetX = source.X + (target.X - source.X) * 0.75 - 32,
                TargetY = source.Y + (target.Y - source.Y) * 0.75 - 15,
            };
            try
            {
                _stack.PushOrMerge(new CreateGraphPortalPairCommand(OpenConnection, connection.GraphKind, pair));
                var visual = editor.AddPortalPairVisual(pair);
                editor.SelectPortalEndpoint(visual.Source);
                StatusText.Text = "Inserted bridge pair " + label + ".";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Bridge insertion failed";
                MessageBox.Show(this, "Could not insert bridge pair:\n\n" + ex.Message,
                    "Bridge Pair", MessageBoxButton.OK, MessageBoxImage.Warning);
                ReloadAllFromDb();
            }
        }

        private static string NextPortalLabel(GraphEditorViewModel editor)
        {
            var used = editor.PortalEndpoints.Select(endpoint => endpoint.Label)
                .Where(label => label != null && label.StartsWith("P", StringComparison.OrdinalIgnoreCase))
                .Select(label => int.TryParse(label.Substring(1), out var number) ? number : 0)
                .Where(number => number > 0).ToHashSet();
            var next = 1;
            while (used.Contains(next)) next++;
            return "P" + next;
        }

        private void OnPortalEndpointContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PortalEndpointViewModel endpoint)
                EditorFor(endpoint).SelectPortalEndpoint(endpoint);
        }

        private void OnPortalEndpointMouseDown(object sender, MouseButtonEventArgs e)
        {
            var endpoint = (sender as FrameworkElement)?.DataContext as PortalEndpointViewModel;
            if (endpoint == null || e.ChangedButton != MouseButton.Left) return;
            var editor = EditorFor(endpoint);
            var nodify = NodifyEditorFor(endpoint);
            editor.SelectPortalEndpoint(endpoint);
            _portalDragEndpoint = endpoint;
            _portalDragOriginal = endpoint.Location;
            var zoom = Math.Max(0.0001, endpoint.GraphKind == "session" ? _vm.SessionGraph.ViewportZoom : _vm.PhaseGraph.ViewportZoom);
            var local = e.GetPosition((IInputElement)sender);
            _portalDragOffset = new Point(local.X / zoom, local.Y / zoom);
            _portalDragActive = true;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void OnPortalEndpointMouseMove(object sender, MouseEventArgs e)
        {
            if (!_portalDragActive || !ReferenceEquals((sender as FrameworkElement)?.DataContext, _portalDragEndpoint) ||
                e.LeftButton != MouseButtonState.Pressed) return;
            var endpoint = _portalDragEndpoint;
            var nodify = NodifyEditorFor(endpoint);
            var zoom = Math.Max(0.0001, endpoint.GraphKind == "session" ? _vm.SessionGraph.ViewportZoom : _vm.PhaseGraph.ViewportZoom);
            var point = nodify.MouseLocation;
            endpoint.Location = new Point(point.X - _portalDragOffset.X, point.Y - _portalDragOffset.Y);
            e.Handled = true;
        }

        private void OnPortalEndpointMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_portalDragActive || !ReferenceEquals((sender as FrameworkElement)?.DataContext, _portalDragEndpoint) ||
                e.ChangedButton != MouseButton.Left) return;
            var endpoint = _portalDragEndpoint;
            _portalDragActive = false;
            ((UIElement)sender).ReleaseMouseCapture();
            var current = endpoint.Location;
            if (current != _portalDragOriginal)
            {
                try
                {
                    _stack.PushOrMerge(new MoveGraphPortalEndpointCommand(OpenConnection, endpoint.GraphKind,
                        endpoint.Pair.Id, endpoint.IsSource,
                        new Point2(_portalDragOriginal.X, _portalDragOriginal.Y),
                        new Point2(current.X, current.Y)));
                }
                catch (Exception ex)
                {
                    endpoint.Location = _portalDragOriginal;
                    StatusText.Text = "Bridge move failed";
                    MessageBox.Show(this, "Could not save bridge endpoint position:\n\n" + ex.Message,
                        "Bridge Pair", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            _portalDragEndpoint = null;
            e.Handled = true;
        }

        private void OnRemoveBridgePairFromMenu(object sender, RoutedEventArgs e)
        {
            var menu = (sender as MenuItem)?.Parent as ContextMenu;
            var target = menu?.PlacementTarget as FrameworkElement;
            var data = target?.DataContext ?? (sender as FrameworkElement)?.DataContext;
            var endpoint = data as PortalEndpointViewModel;
            var connection = data as ConnectionViewModel;
            var pair = endpoint?.Pair ?? connection?.PortalPair;
            if (pair == null) return;
            RemovePortalPair(pair);
        }

        private void RemovePortalPair(GraphPortalPairViewModel pair)
        {
            var editor = EditorFor(pair.Source);
            try
            {
                var snapshot = new GraphPortalPairDefinition
                {
                    Id = pair.Id, GraphId = pair.GraphOwnerId, EdgeId = pair.EdgeId, Label = pair.Label,
                    ColorSlot = pair.ColorSlot, SourceX = pair.Source.Location.X, SourceY = pair.Source.Location.Y,
                    TargetX = pair.Target.Location.X, TargetY = pair.Target.Location.Y,
                };
                _stack.PushOrMerge(new RemoveGraphPortalPairCommand(OpenConnection, pair.GraphKind, snapshot));
                editor.RemovePortalPairVisual(pair);
                StatusText.Text = "Removed bridge pair " + pair.Label + ".";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Bridge removal failed";
                MessageBox.Show(this, "Could not remove bridge pair:\n\n" + ex.Message,
                    "Bridge Pair", MessageBoxButton.OK, MessageBoxImage.Warning);
                ReloadAllFromDb();
            }
        }

        private void DeleteSelectedPortalIfAny(GraphEditorViewModel editor)
        {
            var endpoint = editor.PortalEndpoints.FirstOrDefault(item => item.IsSelected);
            if (endpoint != null) RemovePortalPair(endpoint.Pair);
        }
    }
}
