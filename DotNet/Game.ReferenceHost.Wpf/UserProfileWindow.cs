using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;
using TruthCardGame.Profile;
using TruthCardGame.Profile.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Ticket 12: the persistent User/Test Profile surface. Kinks show
    /// Love/Like/Torture/Don't Consent (exactly one when configured) with
    /// Unconfigured as the visible default; Equipment is grouped by category
    /// with owned checkboxes; capabilities have available checkboxes.
    /// Persists to UserProfile.db in per-user app data — restart preserves
    /// everything. Adding a new Kink in content after the profile exists
    /// simply shows it as Unconfigured.
    /// </summary>
    public partial class UserProfileWindow : Window
    {
        private GameContentDefinition _content;
        private bool _suppressEvents;

        // Matches the hardcoded foreground every other window uses (#FFDDDDDD).
        // Never look this up via FindResource — a wrong key there is an
        // unhandled-exception process kill on the UI thread.
        private static readonly System.Windows.Media.SolidColorBrush RowTextBrush =
            new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFDDDDDD"));

        public UserProfileWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => ReloadSafelyPublic();
        }

        private static SqliteConnection OpenProfileConnection()
        {
            var path = UserProfilePaths.ProfileDatabasePath();
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            var connection = new SqliteConnection("Data Source=" + path);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Full reload wrapped so ANY failure (content load, profile DB, UI
        /// build) surfaces as a message instead of an unhandled UI-thread
        /// exception that kills the whole application. Public so the
        /// Workbench can refresh the window on reopen (catalogs may have
        /// changed while it was closed).
        /// </summary>
        public void ReloadSafelyPublic()
        {
            try
            {
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to load the profile:\n\n" + ex.Message,
                    "Profile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Reload()
        {
            try
            {
                var contentPath = ReferencePlayerWindow.ResolveDatabasePath();
                using (var connection = new SqliteConnection("Data Source=" + contentPath))
                {
                    _content = GameContentSnapshotLoader.Load(connection);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to load content catalogs:\n\n" + ex.Message,
                    "Profile", MessageBoxButton.OK, MessageBoxImage.Error);
                _content = new GameContentDefinition();
            }

            UserProfileData profile;
            using (var connection = OpenProfileConnection())
            {
                ProfileStore.EnsureSchema(connection);
                profile = ProfileStore.Load(connection);
            }

            BuildKinkList(profile);
            BuildEquipmentList(profile);
            BuildCapabilityList(profile);
        }

        private void BuildKinkList(UserProfileData profile)
        {
            _suppressEvents = true;
            try
            {
                KinkList.Items.Clear();
                foreach (var kink in _content.KinkDefinitions.OrderBy(k => k.SortOrder).ThenBy(k => k.Title))
                {
                    var kinkId = kink.Id;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };

                    var title = new TextBlock { Text = kink.Title, Width = 150, VerticalAlignment = VerticalAlignment.Center, Foreground = RowTextBrush };
                    var combo = new ComboBox { Width = 132 };
                    combo.Items.Add("(Unconfigured)");
                    combo.Items.Add("Love");
                    combo.Items.Add("Like");
                    combo.Items.Add("Torture");
                    combo.Items.Add("Don't Consent");
                    combo.SelectedIndex = profile.KinkPreferences.TryGetValue(kinkId, out var preference) ? 1 + (int)preference : 0;
                    combo.SelectionChanged += (_, _) =>
                    {
                        if (_suppressEvents) return;
                        KinkPreference? chosen = combo.SelectedIndex <= 0 ? (KinkPreference?)null : (KinkPreference)(combo.SelectedIndex - 1);
                        try
                        {
                            using (var connection = OpenProfileConnection())
                            {
                                ProfileStore.EnsureSchema(connection);
                                ProfileStore.SetKinkPreference(connection, kinkId, chosen);
                            }
                            StatusText.Text = chosen == null
                                ? $"{kink.Title} → Unconfigured"
                                : $"{kink.Title} → {chosen}";
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, "Failed to save the kink preference:\n\n" + ex.Message,
                                "Profile", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };

                    row.Children.Add(title);
                    row.Children.Add(combo);
                    KinkList.Items.Add(row);
                }
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void BuildEquipmentList(UserProfileData profile)
        {
            _suppressEvents = true;
            try
            {
                EquipmentList.Items.Clear();
                var groups = _content.EquipmentDefinitions
                    .GroupBy(e => string.IsNullOrEmpty(e.Category) ? "Misc" : e.Category)
                    .OrderBy(g => g.Key);
                foreach (var group in groups)
                {
                    EquipmentList.Items.Add(new TextBlock
                    {
                        Text = group.Key,
                        FontWeight = FontWeights.Bold,
                        Foreground = RowTextBrush,
                        Margin = new Thickness(2, 6, 2, 2),
                    });
                    foreach (var equipment in group.OrderBy(e => e.SortOrder).ThenBy(e => e.Title))
                    {
                        var equipmentId = equipment.Id;
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 1, 2, 1) };
                        var box = new CheckBox { IsChecked = profile.OwnedEquipmentIds.Contains(equipmentId), VerticalAlignment = VerticalAlignment.Center };
                        box.Checked += (_, _) => SaveEquipment(equipmentId, equipment.Title, true);
                        box.Unchecked += (_, _) => SaveEquipment(equipmentId, equipment.Title, false);
                        var title = new TextBlock { Text = equipment.Title, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                        row.Children.Add(box);
                        row.Children.Add(title);
                        EquipmentList.Items.Add(row);
                    }
                }
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void BuildCapabilityList(UserProfileData profile)
        {
            _suppressEvents = true;
            try
            {
                CapabilityList.Items.Clear();
                foreach (var capability in _content.SmartToyCapabilityDefinitions.OrderBy(c => c.SortOrder).ThenBy(c => c.Title))
                {
                    var capabilityId = capability.Id;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                    var box = new CheckBox { IsChecked = profile.AvailableCapabilityIds.Contains(capabilityId), VerticalAlignment = VerticalAlignment.Center };
                    box.Checked += (_, _) => SaveCapability(capabilityId, capability.Title, true);
                    box.Unchecked += (_, _) => SaveCapability(capabilityId, capability.Title, false);
                    var title = new TextBlock { Text = capability.Title, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    row.Children.Add(box);
                    row.Children.Add(title);
                    CapabilityList.Items.Add(row);
                }
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void SaveEquipment(string equipmentId, string title, bool owned)
        {
            if (_suppressEvents) return;
            try
            {
                using (var connection = OpenProfileConnection())
                {
                    ProfileStore.EnsureSchema(connection);
                    ProfileStore.SetEquipmentOwned(connection, equipmentId, owned);
                }
                StatusText.Text = $"{title} {(owned ? "owned" : "not owned")}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to save equipment ownership:\n\n" + ex.Message,
                    "Profile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveCapability(string capabilityId, string title, bool available)
        {
            if (_suppressEvents) return;
            try
            {
                using (var connection = OpenProfileConnection())
                {
                    ProfileStore.EnsureSchema(connection);
                    ProfileStore.SetCapabilityAvailable(connection, capabilityId, available);
                }
                StatusText.Text = $"{title} {(available ? "available" : "unavailable")}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to save capability availability:\n\n" + ex.Message,
                    "Profile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRefresh(object sender, RoutedEventArgs e) => ReloadSafelyPublic();
    }
}
