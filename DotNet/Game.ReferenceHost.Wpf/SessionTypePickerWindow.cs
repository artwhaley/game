using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Ticket 16 proof UI: pick a Session Type to play. Shows eligibility
    /// status (missing capabilities) and the eligible session count per type.
    /// </summary>
    public partial class SessionTypePickerWindow : Window
    {
        public string SelectedTitle { get; private set; }

        public SessionTypePickerWindow(List<string> statusLines, List<string> eligibleTypeTitles)
        {
            InitializeComponent();
            StatusList.ItemsSource = statusLines;
            TypeBox.ItemsSource = eligibleTypeTitles;
            if (eligibleTypeTitles.Count > 0) TypeBox.SelectedIndex = 0;
        }

        private void OnStart(object sender, RoutedEventArgs e)
        {
            SelectedTitle = TypeBox.SelectedItem as string;
            if (string.IsNullOrEmpty(SelectedTitle)) return;
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
