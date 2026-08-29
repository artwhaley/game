using System.Windows;
using System.Windows.Controls;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Small dark-themed modal text input used by catalog/card creation.
    /// Replaces Microsoft.VisualBasic.InputBox (a WinForms API that does not
    /// belong in this WPF host and behaved unreliably here).
    /// </summary>
    public partial class TextInputDialog : Window
    {
        /// <summary>The text the user confirmed, or null when canceled.</summary>
        public string InputText { get; private set; }

        private readonly TextBox _box;

        public TextInputDialog(string title, string label, string initialText = "")
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = System.Windows.Media.Brushes.Transparent;

            var panel = new StackPanel { Margin = new Thickness(12) };

            var labelText = new TextBlock
            {
                Text = label,
                Foreground = FindResource("TextBrush") as System.Windows.Media.Brush,
                Margin = new Thickness(0, 0, 0, 4),
            };
            _box = new TextBox
            {
                Text = initialText ?? "",
                Padding = new Thickness(3, 1, 3, 1),
            };
            _box.Style = FindResource("DarkTextBox") as Style;
            Loaded += (_, _) =>
            {
                _box.Focus();
                _box.SelectAll();
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            var ok = new Button { Content = "OK", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(0, 0, 6, 0) };
            ok.Style = FindResource("DarkButton") as Style;
            ok.Click += (_, _) =>
            {
                InputText = _box.Text;
                DialogResult = true;
            };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 2, 14, 2) };
            cancel.Style = FindResource("DarkButton") as Style;
            cancel.Click += (_, _) => { DialogResult = false; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            panel.Children.Add(labelText);
            panel.Children.Add(_box);
            panel.Children.Add(buttons);

            // Dark chrome around the small dialog content.
            var border = new Border
            {
                Background = FindResource("PanelBrush") as System.Windows.Media.Brush,
                BorderBrush = FindResource("BorderBrushColor") as System.Windows.Media.Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new System.Windows.CornerRadius(4),
                Child = panel,
            };
            Content = border;

            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    InputText = _box.Text;
                    DialogResult = true;
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    DialogResult = false;
                }
            };
        }
    }
}
