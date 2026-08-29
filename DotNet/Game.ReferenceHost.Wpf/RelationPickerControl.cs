using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Stable-ID display choice used by all relation pickers.</summary>
    public sealed class RelationChoice
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string SearchText => (Id + " " + DisplayName).Trim();
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Searchable multi-select picker. The selected value is always the stable
    /// relation ID; display names are presentation only, so duplicate titles
    /// remain independently selectable.
    /// </summary>
    public sealed class RelationPickerControl : UserControl
    {
        private readonly ObservableCollection<RelationChoice> _choices = new ObservableCollection<RelationChoice>();
        private readonly ObservableCollection<RelationChoice> _selected = new ObservableCollection<RelationChoice>();
        private readonly ListBox _results;
        private readonly TextBox _search;
        private readonly Popup _popup;
        private readonly ICollectionView _view;
        private bool _suppress;

        public RelationPickerControl()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3E)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3),
            };
            var root = new DockPanel();

            var add = new Button { Content = "+", Padding = new Thickness(5, 0, 5, 0), ToolTip = "Add relation" };
            add.Click += (_, _) => TogglePopup();
            DockPanel.SetDock(add, Dock.Right);
            root.Children.Add(add);

            var selectedItems = new ItemsControl { ItemsSource = _selected, Margin = new Thickness(0, 0, 3, 0) };
            selectedItems.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
            selectedItems.ItemTemplate = BuildChipTemplate();
            root.Children.Add(selectedItems);
            border.Child = root;
            Content = border;

            _search = new TextBox { Width = 220, Padding = new Thickness(3), ToolTip = "Search by title or stable ID" };
            _search.TextChanged += (_, _) => _view.Refresh();
            _search.PreviewKeyDown += OnSearchKeyDown;
            _results = new ListBox { Width = 260, MaxHeight = 180, DisplayMemberPath = nameof(RelationChoice.DisplayName) };
            _results.MouseDoubleClick += (_, _) => AddHighlighted();
            _results.PreviewKeyDown += OnResultsKeyDown;
            _view = new ListCollectionView(_choices);
            _view.Filter = FilterChoice;
            _results.ItemsSource = _view;

            var popupPanel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2F)) };
            popupPanel.Children.Add(_search);
            popupPanel.Children.Add(_results);
            _popup = new Popup
            {
                Child = popupPanel,
                PlacementTarget = add,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
            };
            _popup.Closed += (_, _) => _search.Clear();
        }

        public event RoutedEventHandler SelectionChanged;

        public IReadOnlyList<string> SelectedIds => _selected.Select(item => item.Id).ToList();

        public void SetItems(IEnumerable<RelationChoice> choices, IEnumerable<string> selectedIds)
        {
            _suppress = true;
            try
            {
                _choices.Clear();
                foreach (var choice in choices ?? Enumerable.Empty<RelationChoice>())
                    if (choice != null && !string.IsNullOrWhiteSpace(choice.Id)) _choices.Add(choice);

                _selected.Clear();
                var wanted = new HashSet<string>(selectedIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
                foreach (var choice in _choices)
                    if (wanted.Contains(choice.Id)) _selected.Add(choice);
                _view.Refresh();
            }
            finally
            {
                _suppress = false;
            }
        }

        public void SetSelectedIds(IEnumerable<string> selectedIds)
        {
            var wanted = new HashSet<string>(selectedIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _suppress = true;
            try
            {
                _selected.Clear();
                foreach (var choice in _choices)
                    if (wanted.Contains(choice.Id)) _selected.Add(choice);
                _view.Refresh();
            }
            finally
            {
                _suppress = false;
            }
        }

        private DataTemplate BuildChipTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x50)));
            border.SetValue(Border.MarginProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(4, 1, 2, 1));
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty, new Binding(nameof(RelationChoice.DisplayName)));
            row.AppendChild(label);
            var remove = new FrameworkElementFactory(typeof(Button));
            remove.SetValue(Button.ContentProperty, "×");
            remove.SetValue(Button.PaddingProperty, new Thickness(3, 0, 3, 0));
            remove.SetValue(Button.MarginProperty, new Thickness(3, 0, 0, 0));
            remove.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnRemoveClick));
            row.AppendChild(remove);
            border.AppendChild(row);
            return new DataTemplate { VisualTree = border };
        }

        private bool FilterChoice(object value)
        {
            var choice = value as RelationChoice;
            if (choice == null || _selected.Any(item => item.Id == choice.Id)) return false;
            var query = (_search.Text ?? "").Trim();
            return query.Length == 0 || choice.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TogglePopup()
        {
            _popup.IsOpen = !_popup.IsOpen;
            if (_popup.IsOpen)
            {
                _view.Refresh();
                _search.Focus();
            }
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                _results.Focus();
                if (_results.SelectedIndex < 0 && _results.Items.Count > 0) _results.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                AddHighlighted();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _popup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void OnResultsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddHighlighted();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _popup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void AddHighlighted()
        {
            if (!(_results.SelectedItem is RelationChoice choice)) return;
            if (_selected.All(item => item.Id != choice.Id))
            {
                _selected.Add(choice);
                _view.Refresh();
                if (!_suppress) SelectionChanged?.Invoke(this, new RoutedEventArgs());
            }
            _search.Focus();
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is RelationChoice choice)
            {
                _selected.Remove(choice);
                _view.Refresh();
                if (!_suppress) SelectionChanged?.Invoke(this, new RoutedEventArgs());
            }
        }
    }
}
