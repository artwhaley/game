using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;
using TruthCardGame.Profile;
using TruthCardGame.Profile.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Milestone B authoring surfaces (Tickets 11–16, round-2 rework):
    /// - Catalogs library mode (Session Types, Card Tags, Kinks, Equipment,
    ///   Smart Toy capabilities) with referenced-deletion blocking;
    /// - Cards library mode with search/New/Duplicate/Delete and the Card
    ///   editor in the INSPECTOR (fully buffered: Save Card / Revert apply
    ///   or discard title, body, relations, AND the Action sequence as one
    ///   undo step). The Phase graph below is never hidden;
    /// - Persistent User/Test Profile window backed by UserProfile.db;
    /// - Session weighting editors on the SessionStart inspector;
    /// - Selection diagnostics for the selected Phase;
    /// - Consumer-style Play-by-Type session start.
    /// All content edits flow through the semantic undo command stack.
    /// </summary>
    public partial class MainWindow
    {
        private bool _syncingCardEditor;
        private bool _syncingWeighting;
        private bool _syncingCatalogEditor;
        private string _selectedCardFolderId = "";
        private string _activeCardId;
        private Point _cardTreeDragStart;
        private bool _cardTreeDragInProgress;
        private readonly Dictionary<string, CardFolderTreeNode> _cardFolderNodesById = new Dictionary<string, CardFolderTreeNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CardFolderTreeNode> _cardTreeNodesByCardId = new Dictionary<string, CardFolderTreeNode>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedCardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedCardFolderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Flattened visual order of card nodes for Shift+click ranges.</summary>
        private readonly List<string> _cardVisualOrder = new List<string>();
        private string _cardSelectionAnchorId;
        /// <summary>Members added by the most recent Shift+click range, so the
        /// next Shift+click can replace just that range.</summary>
        private readonly HashSet<string> _lastCardRangeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const string UnassignedFolderId = "__unassigned__";
        private bool _restoringCardTreeSelection;
        private bool _cardTreeEverBuilt;
        private UserProfileWindow _profileWindow;
        private CardEditBuffer _cardBuffer;

        internal bool IsCardBufferDirty => _cardBuffer?.IsDirty == true;
        internal string DirtyCardTitle => _cardBuffer?.Title ?? "(untitled)";

        internal void FollowRuntimeCard(CardDefinition card)
        {
            if (card == null || IsCardBufferDirty) return;
            OnShowCardsLibrary(this, new RoutedEventArgs());
            SelectTreeCard(card.Id, true);
        }

        // ---------- library mode switching (extends SetLibraryMode) ----------

        private void OnShowCardsLibrary(object sender, RoutedEventArgs e)
        {
            SessionLibraryPanel.Visibility = Visibility.Collapsed;
            PhaseLibraryPanel.Visibility = Visibility.Collapsed;
            CardsLibraryPanel.Visibility = Visibility.Visible;
            CatalogsLibraryPanel.Visibility = Visibility.Collapsed;
            SessionModeText.Text = "Cards";
            BindCardList();
        }

        private void OnShowCatalogsLibrary(object sender, RoutedEventArgs e)
        {
            SessionLibraryPanel.Visibility = Visibility.Collapsed;
            PhaseLibraryPanel.Visibility = Visibility.Collapsed;
            CardsLibraryPanel.Visibility = Visibility.Collapsed;
            CatalogsLibraryPanel.Visibility = Visibility.Visible;
            SessionModeText.Text = "Catalogs";
            BindCatalogs();
        }

        private void OnShowProfile(object sender, RoutedEventArgs e)
        {
            if (_profileWindow == null || !_profileWindow.IsLoaded)
            {
                _profileWindow = new UserProfileWindow();
                _profileWindow.Owner = this;
            }
            // Re-reading the catalogs on every activation keeps the profile
            // window in step with catalog edits made while it was open.
            if (!_profileWindow.IsVisible)
            {
                _profileWindow.ReloadSafelyPublic();
            }
            _profileWindow.Show();
            if (_profileWindow.WindowState == WindowState.Minimized) _profileWindow.WindowState = WindowState.Normal;
            _profileWindow.Activate();
        }

        /// <summary>Hides the Milestone B library drawers (session/phase switch paths call this).</summary>
        private void HideMilestoneBLibraryPanels()
        {
            CardsLibraryPanel.Visibility = Visibility.Collapsed;
            CatalogsLibraryPanel.Visibility = Visibility.Collapsed;
        }

        // ---------- Ticket 11: catalogs ----------

        private static readonly string[] MilestoneBCatalogKinds =
        {
            "Session Types", "Card Tags", "Kinks", "Equipment", "Smart Toys",
            "Dialog Tags", "Dialog Snippets"
        };

        private string SelectedCatalogKind => CatalogKindList.SelectedItem as string;

        private void BindCatalogs()
        {
            if (CatalogKindList.ItemsSource == null)
            {
                CatalogKindList.ItemsSource = MilestoneBCatalogKinds;
                CatalogKindList.SelectedIndex = 0;
            }
            BindCatalogEntries();
        }

        private void OnCatalogKindChanged(object sender, SelectionChangedEventArgs e)
        {
            BindCatalogEntries();
            SyncCatalogEditor();
        }

        private void BindCatalogEntries()
        {
            var kind = SelectedCatalogKind;
            var selectedId = CatalogEntryId(CatalogEntryList.SelectedItem);
            var query = (CatalogSearchBox?.Text ?? "").Trim();
            CatalogEntryList.DisplayMemberPath = kind == "Dialog Snippets"
                ? nameof(DialogSnippetDefinition.Name) : nameof(CardTagDefinition.Title);
            switch (kind)
            {
                case "Session Types":
                    CatalogEntryList.ItemsSource = _vm.Content.SessionTypes.Where(t => MatchesCatalog(t.Id, t.Title, null, null, query)).OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Card Tags":
                    CatalogEntryList.ItemsSource = _vm.Content.CardTagDefinitions.Where(t => MatchesCatalog(t.Id, t.Title, null, null, query)).OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Kinks":
                    CatalogEntryList.ItemsSource = _vm.Content.KinkDefinitions.Where(t => MatchesCatalog(t.Id, t.Title, t.Description, null, query)).OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Equipment":
                    CatalogEntryList.ItemsSource = _vm.Content.EquipmentDefinitions.Where(t => MatchesCatalog(t.Id, t.Title, null, t.Category, query)).OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Smart Toys":
                    CatalogEntryList.ItemsSource = _vm.Content.SmartToyCapabilityDefinitions.Where(t => MatchesCatalog(t.Id, t.Title, null, t.Category, query)).OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Dialog Tags":
                    CatalogEntryList.ItemsSource = _vm.Content.DialogTags.Where(t => MatchesCatalog(t.Id, t.Title, null, null, query)).OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Dialog Snippets":
                {
                    var dialogTagNames = _vm.Content.DialogTags.ToDictionary(tag => tag.Id, tag => tag.Title ?? "");
                    CatalogEntryList.ItemsSource = _vm.Content.DialogSnippets
                        .Where(t => MatchesCatalog(t.Id, t.Name, t.Text,
                            string.Join(" ", (t.DialogTagIds ?? new List<string>())
                                .Select(id => dialogTagNames.TryGetValue(id, out var name) ? name : id)), query))
                        .OrderBy(t => t.SortOrder).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    break;
                }
            }
            if (selectedId != null) SelectCatalogEntry(selectedId);
            if (CatalogEntryList.SelectedItem == null && CatalogEntryList.Items.Count > 0)
                CatalogEntryList.SelectedIndex = 0;
        }

        private void OnCatalogSearchChanged(object sender, TextChangedEventArgs e) => BindCatalogEntries();

        private static bool MatchesCatalog(string id, string title, string description, string category, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            return (id + " " + title + " " + description + " " + category)
                .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CatalogEntryId(object entry)
        {
            if (entry is SessionTypeDefinition session) return session.Id;
            if (entry is CardTagDefinition tag) return tag.Id;
            if (entry is KinkDefinition kink) return kink.Id;
            if (entry is EquipmentDefinition equipment) return equipment.Id;
            if (entry is SmartToyCapabilityDefinition capability) return capability.Id;
            if (entry is DialogTagDefinition dialogTag) return dialogTag.Id;
            if (entry is DialogSnippetDefinition dialogSnippet) return dialogSnippet.Id;
            return null;
        }

        private void OnCatalogEntryChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCatalogUsageText();
            SyncCatalogEditor();
        }

        private void SyncCatalogEditor()
        {
            _syncingCatalogEditor = true;
            try
            {
                var entry = CatalogEntryList?.SelectedItem;
                CatalogEditorPanel.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
                if (entry == null) return;
                CatalogTitleBox.Text = CatalogTitle(entry);
                var isKink = entry is KinkDefinition;
                var isSnippet = entry is DialogSnippetDefinition;
                var isCategory = entry is EquipmentDefinition || entry is SmartToyCapabilityDefinition;
                var isSessionType = entry is SessionTypeDefinition;
                var isDialogSnippet = entry is DialogSnippetDefinition;
                CatalogDescriptionLabel.Text = isSnippet ? "Text" : "Description";
                CatalogDescriptionLabel.Visibility = isKink || isSnippet ? Visibility.Visible : Visibility.Collapsed;
                CatalogDescriptionBox.Visibility = isKink || isSnippet ? Visibility.Visible : Visibility.Collapsed;
                CatalogCategoryLabel.Visibility = isCategory ? Visibility.Visible : Visibility.Collapsed;
                CatalogCategoryBox.Visibility = isCategory ? Visibility.Visible : Visibility.Collapsed;
                CatalogCapabilityLabel.Text = isDialogSnippet ? "Dialog tags" : "Required Smart Toy capabilities";
                CatalogCapabilityLabel.Visibility = isSessionType || isDialogSnippet ? Visibility.Visible : Visibility.Collapsed;
                CatalogCapabilityPicker.Visibility = isSessionType || isDialogSnippet ? Visibility.Visible : Visibility.Collapsed;
                CatalogDescriptionBox.Text = (entry as KinkDefinition)?.Description
                    ?? (entry as DialogSnippetDefinition)?.Text ?? "";
                CatalogCategoryBox.Text = (entry as EquipmentDefinition)?.Category
                    ?? (entry as SmartToyCapabilityDefinition)?.Category ?? "";
                var relationItems = isDialogSnippet
                    ? _vm.Content.DialogTags.Select(tag => new RelationChoice
                    {
                        Id = tag.Id, DisplayName = tag.Title
                    })
                    : _vm.Content.SmartToyCapabilityDefinitions.Select(capability => new RelationChoice
                    {
                        Id = capability.Id, DisplayName = capability.Title
                    });
                IEnumerable<string> selectedRelations = Enumerable.Empty<string>();
                if (isDialogSnippet)
                    selectedRelations = ((DialogSnippetDefinition)entry).DialogTagIds;
                else if (entry is SessionTypeDefinition sessionType)
                    selectedRelations = sessionType.RequiredCapabilityIds;
                CatalogCapabilityPicker.SetItems(relationItems, selectedRelations);
            }
            finally
            {
                _syncingCatalogEditor = false;
            }
        }

        private static string CatalogTitle(object entry)
        {
            if (entry is SessionTypeDefinition session) return session.Title;
            if (entry is CardTagDefinition tag) return tag.Title;
            if (entry is KinkDefinition kink) return kink.Title;
            if (entry is EquipmentDefinition equipment) return equipment.Title;
            if (entry is SmartToyCapabilityDefinition capability) return capability.Title;
            if (entry is DialogTagDefinition dialogTag) return dialogTag.Title;
            if (entry is DialogSnippetDefinition dialogSnippet) return dialogSnippet.Name;
            return "";
        }

        private CatalogEntryEdit CaptureCatalogEdit(object entry)
        {
            if (entry is SessionTypeDefinition session)
                return new CatalogEntryEdit { Kind = CatalogKinds.SessionType, Id = session.Id, Title = session.Title, SortOrder = session.SortOrder, RequiredCapabilityIds = new List<string>(session.RequiredCapabilityIds) };
            if (entry is CardTagDefinition tag)
                return new CatalogEntryEdit { Kind = CatalogKinds.CardTag, Id = tag.Id, Title = tag.Title, SortOrder = tag.SortOrder };
            if (entry is KinkDefinition kink)
                return new CatalogEntryEdit { Kind = CatalogKinds.Kink, Id = kink.Id, Title = kink.Title, Description = kink.Description, SortOrder = kink.SortOrder };
            if (entry is EquipmentDefinition equipment)
                return new CatalogEntryEdit { Kind = CatalogKinds.Equipment, Id = equipment.Id, Title = equipment.Title, Category = equipment.Category, SortOrder = equipment.SortOrder };
            if (entry is SmartToyCapabilityDefinition capability)
                return new CatalogEntryEdit { Kind = CatalogKinds.SmartToyCapability, Id = capability.Id, Title = capability.Title, Category = capability.Category, SortOrder = capability.SortOrder };
            if (entry is DialogTagDefinition dialogTag)
                return new CatalogEntryEdit { Kind = CatalogKinds.DialogTag, Id = dialogTag.Id, Title = dialogTag.Title, SortOrder = dialogTag.SortOrder };
            if (entry is DialogSnippetDefinition dialogSnippet)
                return new CatalogEntryEdit { Kind = CatalogKinds.DialogSnippet, Id = dialogSnippet.Id, Title = dialogSnippet.Name, Description = dialogSnippet.Text,
                    SortOrder = dialogSnippet.SortOrder, DialogTagIds = new List<string>(dialogSnippet.DialogTagIds) };
            return null;
        }

        private void OnCatalogEditorChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingCatalogEditor) return;
            UpdateCatalogUsageText();
        }

        private void OnSaveCatalogEntry(object sender, RoutedEventArgs e)
        {
            var entry = CatalogEntryList.SelectedItem;
            var oldValue = CaptureCatalogEdit(entry);
            if (oldValue == null) return;
            var newValue = new CatalogEntryEdit
            {
                Kind = oldValue.Kind, Id = oldValue.Id, Title = CatalogTitleBox.Text?.Trim() ?? "",
                Description = CatalogDescriptionBox.Text ?? "", Category = CatalogCategoryBox.Text?.Trim() ?? "",
                SortOrder = oldValue.SortOrder, RequiredCapabilityIds = CatalogCapabilityPicker.SelectedIds.ToList(),
                DialogTagIds = CatalogCapabilityPicker.SelectedIds.ToList()
            };
            if (string.IsNullOrWhiteSpace(newValue.Title))
            {
                MessageBox.Show(this, "A catalog title is required.", "Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            PushOrMergeWithReload(new UpdateCatalogEntryCommand(OpenConnection, oldValue, newValue), () =>
            {
                ApplyCatalogEdit(entry, newValue);
                BindCatalogEntries();
                SelectCatalogEntry(newValue.Id);
                BindSessionTypeBox();
                if (newValue.Kind == CatalogKinds.SmartToyCapability || newValue.Kind == CatalogKinds.DialogTag)
                    RefreshActionAuthoringCatalogs();
                StatusText.Text = "Saved catalog entry.";
            });
        }

        private static void ApplyCatalogEdit(object entry, CatalogEntryEdit value)
        {
            if (entry is SessionTypeDefinition session)
            {
                session.Title = value.Title; session.RequiredCapabilityIds = new List<string>(value.RequiredCapabilityIds ?? new List<string>()); return;
            }
            if (entry is CardTagDefinition tag) { tag.Title = value.Title; return; }
            if (entry is KinkDefinition kink) { kink.Title = value.Title; kink.Description = value.Description; return; }
            if (entry is EquipmentDefinition equipment) { equipment.Title = value.Title; equipment.Category = value.Category; return; }
            if (entry is SmartToyCapabilityDefinition capability) { capability.Title = value.Title; capability.Category = value.Category; }
            if (entry is DialogTagDefinition dialogTag) { dialogTag.Title = value.Title; return; }
            if (entry is DialogSnippetDefinition dialogSnippet)
            {
                dialogSnippet.Name = value.Title; dialogSnippet.Text = value.Description ?? "";
                dialogSnippet.DialogTagIds.Clear();
                dialogSnippet.DialogTagIds.AddRange(value.DialogTagIds ?? new List<string>());
            }
        }

        private void UpdateCatalogUsageText()
        {
            var text = "";
            switch (SelectedCatalogKind)
            {
                case "Session Types":
                    if (CatalogEntryList.SelectedItem is SessionTypeDefinition type)
                    {
                        var count = WithConnectionResult(connection => CatalogRepositories.CountSessionsOfType(connection, type.Id));
                        text = $"Referenced by {count} session(s).";
                    }
                    break;
                case "Card Tags":
                    if (CatalogEntryList.SelectedItem is CardTagDefinition tag)
                    {
                        var count = WithConnectionResult(connection => CatalogRepositories.CountCardTagUsage(connection, tag.Id));
                        text = $"Used by {count} card/phase reference(s).";
                    }
                    break;
                case "Kinks":
                    if (CatalogEntryList.SelectedItem is KinkDefinition kink)
                    {
                        var count = WithConnectionResult(connection => CatalogRepositories.CountKinkUsage(connection, kink.Id));
                        text = $"Carried by {count} card(s).";
                    }
                    break;
                case "Equipment":
                    if (CatalogEntryList.SelectedItem is EquipmentDefinition equipment)
                    {
                        var count = WithConnectionResult(connection => CatalogRepositories.CountEquipmentUsage(connection, equipment.Id));
                        text = $"Required by {count} card(s).";
                    }
                    break;
                case "Smart Toys":
                    if (CatalogEntryList.SelectedItem is SmartToyCapabilityDefinition capability)
                    {
                        var usage = WithConnectionResult(connection => CatalogRepositories.GetSmartToyCapabilityUsage(connection, capability.Id));
                        text = "Used by: " + usage.Describe() + ".";
                    }
                    break;
                case "Dialog Tags":
                    if (CatalogEntryList.SelectedItem is DialogTagDefinition dialogTag)
                    {
                        var count = WithConnectionResult(connection => DialogCatalogRepository.GetTagUsage(connection, dialogTag.Id).TotalReferences);
                        text = $"Referenced by {count} snippet/action(s).";
                    }
                    break;
                case "Dialog Snippets":
                    if (CatalogEntryList.SelectedItem is DialogSnippetDefinition dialogSnippet)
                        text = $"Uses {dialogSnippet.DialogTagIds.Count} dialog tag(s).";
                    break;
            }
            CatalogUsageText.Text = text;
        }

        private void OnNewCatalogEntry(object sender, RoutedEventArgs e)
        {
            var kind = SelectedCatalogKind;
            var title = PromptForTitle("New " + kind.TrimEnd('s'), "Title:");
            if (string.IsNullOrWhiteSpace(title)) return;

            var id = StableIds.New();
            var kindKey = CatalogKindOf(kind);
            if (kindKey == CatalogKinds.DialogSnippet)
            {
                var snippet = new DialogSnippetDefinition { Id = id, Name = title, Text = "" };
                PushOrMergeWithReload(new CreateDialogSnippetCommand(OpenConnection, snippet), () =>
                {
                    _vm.Content.DialogSnippets.Add(snippet);
                    BindCatalogEntries();
                    SelectCatalogEntry(id);
                    StatusText.Text = $"Created dialog snippet '{title}'.";
                });
                return;
            }
            if (kindKey == CatalogKinds.DialogTag)
            {
                var tag = new DialogTagDefinition { Id = id, Title = title };
                PushOrMergeWithReload(new CreateDialogTagCommand(OpenConnection, tag), () =>
                {
                    _vm.Content.DialogTags.Add(tag);
                    BindCatalogEntries();
                    SelectCatalogEntry(id);
                    RefreshActionAuthoringCatalogs();
                    StatusText.Text = $"Created dialog tag '{title}'.";
                });
                return;
            }
            PushOrMergeWithReload(new CreateCatalogEntryCommand(OpenConnection, kindKey, id, title), () =>
            {
                // Mirror the DB row into the in-memory snapshot (the same
                // pattern as OnNewSession/OnNewPhase) so the rebind below
                // actually shows the new row — BindCatalogEntries reads from
                // _vm.Content, not the database.
                AddCatalogEntryToContent(kindKey, id, title);
                BindCatalogEntries();
                BindSessionTypeBox();
                SelectCatalogEntry(id);
                if (kindKey == CatalogKinds.SmartToyCapability)
                    RefreshActionAuthoringCatalogs();
                StatusText.Text = $"Created '{title}'.";
            });
        }

        /// <summary>Inserts the freshly created definition into the snapshot the lists bind from.</summary>
        private void AddCatalogEntryToContent(string kindKey, string id, string title)
        {
            switch (kindKey)
            {
                case CatalogKinds.SessionType:
                    _vm.Content.SessionTypes.Add(new SessionTypeDefinition { Id = id, Title = title });
                    break;
                case CatalogKinds.CardTag:
                    _vm.Content.CardTagDefinitions.Add(new CardTagDefinition { Id = id, Title = title });
                    break;
                case CatalogKinds.Kink:
                    _vm.Content.KinkDefinitions.Add(new KinkDefinition { Id = id, Title = title });
                    break;
                case CatalogKinds.Equipment:
                    _vm.Content.EquipmentDefinitions.Add(new EquipmentDefinition { Id = id, Title = title });
                    break;
                case CatalogKinds.SmartToyCapability:
                    _vm.Content.SmartToyCapabilityDefinitions.Add(new SmartToyCapabilityDefinition { Id = id, Title = title });
                    break;
                case CatalogKinds.DialogTag:
                    _vm.Content.DialogTags.Add(new DialogTagDefinition { Id = id, Title = title });
                    break;
                case CatalogKinds.DialogSnippet:
                    _vm.Content.DialogSnippets.Add(new DialogSnippetDefinition { Id = id, Name = title });
                    break;
            }
        }

        private void RemoveCatalogEntryFromContent(string kindKey, string id)
        {
            switch (kindKey)
            {
                case CatalogKinds.SessionType:
                    _vm.Content.SessionTypes.RemoveAll(t => t.Id == id);
                    break;
                case CatalogKinds.CardTag:
                    _vm.Content.CardTagDefinitions.RemoveAll(t => t.Id == id);
                    break;
                case CatalogKinds.Kink:
                    _vm.Content.KinkDefinitions.RemoveAll(t => t.Id == id);
                    break;
                case CatalogKinds.Equipment:
                    _vm.Content.EquipmentDefinitions.RemoveAll(t => t.Id == id);
                    break;
                case CatalogKinds.SmartToyCapability:
                    _vm.Content.SmartToyCapabilityDefinitions.RemoveAll(t => t.Id == id);
                    break;
                case CatalogKinds.DialogTag:
                    _vm.Content.DialogTags.RemoveAll(t => t.Id == id);
                    break;
                case CatalogKinds.DialogSnippet:
                    _vm.Content.DialogSnippets.RemoveAll(t => t.Id == id);
                    break;
            }
        }

        /// <summary>Selects the created row so it's immediately visible in the list.</summary>
        private void SelectCatalogEntry(string id)
        {
            for (var i = 0; i < CatalogEntryList.Items.Count; i++)
            {
                if (CatalogEntryList.Items[i] is SessionTypeDefinition t && t.Id == id ||
                    CatalogEntryList.Items[i] is CardTagDefinition tag && tag.Id == id ||
                    CatalogEntryList.Items[i] is KinkDefinition kink && kink.Id == id ||
                    CatalogEntryList.Items[i] is EquipmentDefinition equipment && equipment.Id == id ||
                    CatalogEntryList.Items[i] is SmartToyCapabilityDefinition capability && capability.Id == id ||
                    CatalogEntryList.Items[i] is DialogTagDefinition dialogTag && dialogTag.Id == id ||
                    CatalogEntryList.Items[i] is DialogSnippetDefinition dialogSnippet && dialogSnippet.Id == id)
                {
                    CatalogEntryList.SelectedIndex = i;
                    return;
                }
            }
        }

        private static string CatalogKindOf(string uiKind)
        {
            switch (uiKind)
            {
                case "Session Types": return CatalogKinds.SessionType;
                case "Card Tags": return CatalogKinds.CardTag;
                case "Kinks": return CatalogKinds.Kink;
                case "Equipment": return CatalogKinds.Equipment;
                case "Smart Toys": return CatalogKinds.SmartToyCapability;
                case "Dialog Tags": return CatalogKinds.DialogTag;
                case "Dialog Snippets": return CatalogKinds.DialogSnippet;
                default: throw new InvalidOperationException("Unknown catalog kind '" + uiKind + "'.");
            }
        }

        private void OnDeleteCatalogEntry(object sender, RoutedEventArgs e)
        {
            var kind = SelectedCatalogKind;
            string id;
            string title;
            int usage;
            SmartToyCapabilityUsage capabilityUsage = null;

            switch (kind)
            {
                case "Session Types":
                    if (!(CatalogEntryList.SelectedItem is SessionTypeDefinition type)) return;
                    id = type.Id; title = type.Title;
                    usage = WithConnectionResult(connection => CatalogRepositories.CountSessionsOfType(connection, id));
                    break;
                case "Card Tags":
                    if (!(CatalogEntryList.SelectedItem is CardTagDefinition tag)) return;
                    id = tag.Id; title = tag.Title;
                    usage = WithConnectionResult(connection => CatalogRepositories.CountCardTagUsage(connection, id));
                    break;
                case "Kinks":
                    if (!(CatalogEntryList.SelectedItem is KinkDefinition kink)) return;
                    id = kink.Id; title = kink.Title;
                    usage = WithConnectionResult(connection => CatalogRepositories.CountKinkUsage(connection, id));
                    break;
                case "Equipment":
                    if (!(CatalogEntryList.SelectedItem is EquipmentDefinition equipment)) return;
                    id = equipment.Id; title = equipment.Title;
                    usage = WithConnectionResult(connection => CatalogRepositories.CountEquipmentUsage(connection, id));
                    break;
                case "Smart Toys":
                    if (!(CatalogEntryList.SelectedItem is SmartToyCapabilityDefinition capability)) return;
                    id = capability.Id; title = capability.Title;
                    capabilityUsage = WithConnectionResult(connection => CatalogRepositories.GetSmartToyCapabilityUsage(connection, id));
                    usage = capabilityUsage.TotalReferences;
                    break;
                case "Dialog Tags":
                    if (!(CatalogEntryList.SelectedItem is DialogTagDefinition dialogTag)) return;
                    id = dialogTag.Id; title = dialogTag.Title;
                    usage = WithConnectionResult(connection => DialogCatalogRepository.GetTagUsage(connection, id).TotalReferences);
                    break;
                case "Dialog Snippets":
                    if (!(CatalogEntryList.SelectedItem is DialogSnippetDefinition dialogSnippet)) return;
                    id = dialogSnippet.Id; title = dialogSnippet.Name;
                    usage = 0;
                    break;
                default:
                    return;
            }

            var kindKey = CatalogKindOf(kind);
            var unsavedMessage = UnsavedCatalogReferenceMessage(kindKey, id);
            if (unsavedMessage != null)
            {
                MessageBox.Show(this, unsavedMessage, "Unsaved Card", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (usage > 0)
            {
                var usageMessage = capabilityUsage == null
                    ? $"'{title}' is referenced by {usage} item(s) and cannot be deleted.\n\nRemove the references first."
                    : $"'{title}' cannot be deleted.\n\nUsed by:\n" +
                      $"{capabilityUsage.Cards} Cards\n" +
                      $"{capabilityUsage.SessionTypes} Session Types\n" +
                      $"{capabilityUsage.TimedToyPatternActions} Timed Toy Pattern Actions\n" +
                      $"{capabilityUsage.SetToyPatternActions} Set Toy Pattern Actions";
                MessageBox.Show(this,
                    usageMessage,
                    "Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(this, $"Delete '{title}'?", "Catalog",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            IAuthoringCommand deleteCommand;
            if (kindKey == CatalogKinds.DialogTag)
                deleteCommand = new DeleteDialogTagCommand(OpenConnection, (DialogTagDefinition)CatalogEntryList.SelectedItem);
            else if (kindKey == CatalogKinds.DialogSnippet)
                deleteCommand = new DeleteDialogSnippetCommand(OpenConnection, (DialogSnippetDefinition)CatalogEntryList.SelectedItem);
            else
                deleteCommand = new DeleteCatalogEntryCommand(OpenConnection, kindKey, id, title);
            PushOrMergeWithReload(deleteCommand, () =>
            {
                RemoveCatalogEntryFromContent(kindKey, id);
                BindCatalogEntries();
                BindSessionTypeBox();
                if (kindKey == CatalogKinds.SmartToyCapability || kindKey == CatalogKinds.DialogTag)
                    RefreshActionAuthoringCatalogs();
                StatusText.Text = $"Deleted '{title}'.";
            });
        }

        private void OnDuplicateCatalogEntry(object sender, RoutedEventArgs e)
        {
            if (!(CatalogEntryList.SelectedItem is DialogSnippetDefinition source))
            {
                StatusText.Text = "Only dialog snippets can be duplicated.";
                return;
            }

            var clone = new DialogSnippetDefinition
            {
                Id = StableIds.New(),
                Name = (source.Name ?? "Snippet") + " (copy)",
                Text = source.Text ?? "",
                SortOrder = source.SortOrder
            };
            clone.DialogTagIds.AddRange(source.DialogTagIds ?? new List<string>());
            PushOrMergeWithReload(new CreateDialogSnippetCommand(OpenConnection, clone), () =>
            {
                _vm.Content.DialogSnippets.Add(clone);
                BindCatalogEntries();
                SelectCatalogEntry(clone.Id);
                StatusText.Text = $"Duplicated dialog snippet '{source.Name}'.";
            });
        }

        /// <summary>Pushes a command then runs a UI refresh (catalog CRUD reloads only its list).</summary>
        private void PushOrMergeWithReload(IAuthoringCommand command, Action refresh)
        {
            try
            {
                _stack.PushOrMerge(command);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Persistence error";
                MessageBox.Show(this, "Database write failed:\n\n" + ex.Message,
                    "Workbench", MessageBoxButton.OK, MessageBoxImage.Error);
                ReloadAllFromDb();
                return;
            }
            refresh();
        }

        private string PromptForTitle(string caption, string label)
        {
            var dialog = new TextInputDialog(caption, label);
            dialog.Owner = this;
            return dialog.ShowDialog() == true ? dialog.InputText?.Trim() : null;
        }

        // ---------- Ticket 13 (round 2): cards library + buffered inspector editor ----------

        private void BindCardList()
        {
            // A few headless layout tests switch library modes before the
            // collapsed Cards drawer has materialized its named child.
            if (CardFolderTree == null || _vm?.Content == null) return;
            if (!_restoringCardTreeSelection && CardFolderTree.SelectedItem is CardFolderTreeNode selectedNode && !selectedNode.IsCard)
                _selectedCardFolderId = selectedNode.IsAllCards ? "" : selectedNode.Id;
            BuildCardFolderTree();
        }

        private void BuildCardFolderTree()
        {
            // Snapshot expansion from the live containers before replacing the tree.
            var expandedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hadExpandedSnapshot = CaptureExpandedFolderIds(CardFolderTree, expandedIds);
            if (!hadExpandedSnapshot && !_cardTreeEverBuilt)
                expandedIds.Add(""); // first build: expand the root so the tree is not one collapsed line

            var allCards = new CardFolderTreeNode("", "All Cards", "", true);
            var unassigned = new CardFolderTreeNode(UnassignedFolderId, "UNASSIGNED", "", false, true);
            _cardFolderNodesById.Clear();
            _cardTreeNodesByCardId.Clear();
            _cardVisualOrder.Clear();
            allCards.AddChild(unassigned);
            _cardFolderNodesById[allCards.Id] = allCards;
            _cardFolderNodesById[unassigned.Id] = unassigned;

            var byPath = new Dictionary<string, CardFolderTreeNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in (_vm.Content.CardFolders ?? new List<CardFolderDefinition>())
                .Where(folder => !string.IsNullOrWhiteSpace(folder.Path))
                .OrderBy(folder => folder.Path.Count(ch => ch == '/'))
                .ThenBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase))
            {
                var path = CardRepository.NormalizeFolder(folder.Path);
                if (path.Length == 0 || byPath.ContainsKey(path)) continue;
                var node = new CardFolderTreeNode(folder.Id, folder.Name, path);
                var separator = path.LastIndexOf('/');
                var parentPath = separator < 0 ? "" : path.Substring(0, separator);
                CardFolderTreeNode parent;
                if (!byPath.TryGetValue(parentPath, out parent)) parent = allCards;
                parent.AddChild(node);
                node.IsSelected = _selectedCardFolderIds.Contains(node.Id);
                byPath[path] = node;
                _cardFolderNodesById[node.Id] = node;
            }

            var query = (CardFilter?.Text ?? "").Trim();
            foreach (var card in _vm.Content.Cards
                .Where(card => string.IsNullOrEmpty(query) ||
                    (card.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (card.Id ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (card.FolderPath ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(card => card.Title, StringComparer.OrdinalIgnoreCase))
            {
                var cardPath = CardRepository.NormalizeFolder(card.FolderPath);
                CardFolderTreeNode parent;
                if (!byPath.TryGetValue(cardPath, out parent)) parent = unassigned;
                var cardNode = new CardFolderTreeNode(card.Id,
                    string.IsNullOrWhiteSpace(card.Title) ? "(untitled card)" : card.Title,
                    cardPath, false, parent.IsUnassigned, card);
                cardNode.IsSelected = _selectedCardIds.Contains(card.Id);
                parent.AddChild(cardNode);
                _cardTreeNodesByCardId[card.Id] = cardNode;
                _cardVisualOrder.Add(card.Id);
            }

            CardFolderTree.ItemsSource = new[] { allCards };
            if (!_cardFolderNodesById.ContainsKey(_selectedCardFolderId)) _selectedCardFolderId = "";
            _selectedCardFolderIds.RemoveWhere(id => !_cardFolderNodesById.ContainsKey(id));
            _selectedCardIds.RemoveWhere(id => !_cardTreeNodesByCardId.ContainsKey(id));
            _cardSelectionAnchorId =
                _cardSelectionAnchorId != null && _cardTreeNodesByCardId.ContainsKey(_cardSelectionAnchorId)
                    ? _cardSelectionAnchorId
                    : null;
            var restoreId = _selectedCardFolderId;
            _cardTreeEverBuilt = true;
            CardFolderTree.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => RestoreCardFolderSelection(restoreId, expandedIds)));
        }

        private static bool CaptureExpandedFolderIds(ItemsControl owner, HashSet<string> expandedIds)
        {
            var foundContainer = false;
            var count = owner.Items.Count;
            for (var index = 0; index < count; index++)
            {
                if (!(owner.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem item)) continue;
                foundContainer = true;
                CaptureExpandedFolderIds(item, expandedIds);
            }
            return foundContainer;
        }

        private static void CaptureExpandedFolderIds(TreeViewItem item, HashSet<string> expandedIds)
        {
            if (item.IsExpanded && item.DataContext is CardFolderTreeNode node && !node.IsCard)
                expandedIds.Add(node.Id);
            var count = item.Items.Count;
            for (var index = 0; index < count; index++)
            {
                if (!(item.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem child)) continue;
                CaptureExpandedFolderIds(child, expandedIds);
            }
        }

        private void RestoreCardFolderSelection(string folderId, HashSet<string> expandedIds)
        {
            if (CardFolderTree == null || CardFolderTree.Items.Count == 0) return;
            var root = CardFolderTree.Items[0] as CardFolderTreeNode;
            if (root == null) return;
            _restoringCardTreeSelection = true;
            try
            {
                ApplyCardTreeExpandedState(CardFolderTree, expandedIds);
                CardFolderTreeNode target;
                if (_cardFolderNodesById.TryGetValue(folderId ?? "", out target) && !target.IsCard)
                    SelectCardFolderByAncestorChain(target);
            }
            finally
            {
                _restoringCardTreeSelection = false;
            }
        }

        /// <summary>Expands only the target's ancestors, leaving every other
        /// folder's collapsed state untouched, then selects the target.</summary>
        private bool SelectCardFolderByAncestorChain(CardFolderTreeNode target)
        {
            var chain = new List<CardFolderTreeNode>();
            for (var current = target; current != null; current = current.Parent) chain.Add(current);
            chain.Reverse();
            ItemsControl container = CardFolderTree;
            for (var index = 0; index < chain.Count; index++)
            {
                var item = container.ItemContainerGenerator.ContainerFromItem(chain[index]) as TreeViewItem;
                if (item == null) return false;
                if (index == chain.Count - 1)
                {
                    item.IsSelected = true;
                    item.BringIntoView();
                    return true;
                }
                item.IsExpanded = true;
                item.UpdateLayout();
                container = item;
            }
            return false;
        }

        private static void ApplyCardTreeExpandedState(ItemsControl owner, HashSet<string> expandedIds)
        {
            if (expandedIds.Count == 0) return;
            var count = owner.Items.Count;
            for (var index = 0; index < count; index++)
            {
                if (!(owner.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem item)) continue;
                ApplyCardTreeExpandedState(item, expandedIds);
            }
        }

        private static void ApplyCardTreeExpandedState(TreeViewItem item, HashSet<string> expandedIds)
        {
            if (item.DataContext is CardFolderTreeNode node && !node.IsCard && expandedIds.Contains(node.Id))
                item.IsExpanded = true;
            if (!item.IsExpanded) return;
            item.UpdateLayout();
            var count = item.Items.Count;
            for (var index = 0; index < count; index++)
            {
                if (!(item.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem child)) continue;
                ApplyCardTreeExpandedState(child, expandedIds);
            }
        }

        private string SelectedCardFolderPath()
        {
            CardFolderTreeNode node;
            return _cardFolderNodesById.TryGetValue(_selectedCardFolderId ?? "", out node) ? node.Path : "";
        }

        private CardFolderTreeNode SelectedCardFolderNode()
        {
            CardFolderTreeNode node;
            return _cardFolderNodesById.TryGetValue(_selectedCardFolderId ?? "", out node) && !node.IsCard ? node : null;
        }

        private void OnCardTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as CardFolderTreeNode;
            if (node == null || _restoringCardTreeSelection) return;
            if (node.IsCard)
            {
                // A Ctrl+click toggle must not wipe the rest of the selection when
                // the TreeView re-selects the toggled card natively.
                if (_selectedCardIds.Contains(node.Card.Id)) _activeCardId = node.Card.Id;
                else SelectSingleCard(node, true);
                return;
            }
            // A Ctrl+click on a folder extends the batch; plain folder clicks
            // reset to just that folder.
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && node.IsFolder && _selectedCardFolderIds.Contains(node.Id))
            {
                _selectedCardFolderId = node.Id;
                return;
            }
            SelectSingleFolder(node);
        }

        private void OnCardFilterChanged(object sender, TextChangedEventArgs e)
        {
            BindCardList();
        }

        private void SelectFolder(CardFolderTreeNode node)
        {
            if (node == null || node.IsCard) return;
            ClearCardSelection();
            ClearFolderSelection();
            _activeCardId = null;
            _selectedCardFolderId = node.IsAllCards ? "" : node.Id;
            node.IsSelected = true;
        }

        /// <summary>Plain-click selection of a folder node: resets to just that
        /// folder (also the anchor for future Ctrl+clicks).</summary>
        private void SelectSingleFolder(CardFolderTreeNode node)
        {
            if (node == null || node.IsCard) return;
            ClearCardSelection();
            ClearFolderSelection();
            _activeCardId = null;
            if (node.IsAllCards)
            {
                _selectedCardFolderId = "";
                return; // the virtual root is never part of a batch
            }
            _selectedCardFolderId = node.Id;
            _selectedCardFolderIds.Add(node.Id);
            node.IsSelected = true;
        }

        private void ClearCardSelection()
        {
            foreach (var node in _cardTreeNodesByCardId.Values) node.IsSelected = false;
            _selectedCardIds.Clear();
        }

        private void ClearFolderSelection()
        {
            foreach (var folder in _cardFolderNodesById.Values) folder.IsSelected = false;
            _selectedCardFolderIds.Clear();
        }

        /// <summary>Plain-click selection of one card: resets everything to it.</summary>
        private void SelectSingleCard(CardFolderTreeNode node, bool openEditor)
        {
            if (node == null || !node.IsCard) return;
            ClearCardSelection();
            ClearFolderSelection();
            _selectedCardIds.Add(node.Card.Id);
            node.IsSelected = true;
            _activeCardId = node.Card.Id;
            _cardSelectionAnchorId = node.Card.Id;
            _selectedCardFolderId = node.Parent == null || node.Parent.IsAllCards ? "" : node.Parent.Id;
            if (openEditor && (_cardBuffer == null || _cardBuffer.CardId != node.Card.Id)) OpenCardEditor(node.Card);
        }

        /// <summary>Ctrl+click on a card: toggles it in the mixed selection
        /// (cards and real folders coexist in one batch).</summary>
        private void ToggleTreeSelection(CardFolderTreeNode node)
        {
            if (node == null) return;
            if (node.IsCard)
            {
                if (_selectedCardIds.Contains(node.Card.Id))
                {
                    _selectedCardIds.Remove(node.Card.Id);
                    node.IsSelected = false;
                    if (_activeCardId == node.Card.Id) _activeCardId = _selectedCardIds.FirstOrDefault();
                }
                else
                {
                    _selectedCardIds.Add(node.Card.Id);
                    node.IsSelected = true;
                    _activeCardId = node.Card.Id;
                }
                _cardSelectionAnchorId = node.Card.Id;
            }
            else if (node.IsFolder)
            {
                if (_selectedCardFolderIds.Contains(node.Id))
                {
                    _selectedCardFolderIds.Remove(node.Id);
                    node.IsSelected = false;
                    if (string.Equals(_selectedCardFolderId, node.Id, StringComparison.OrdinalIgnoreCase))
                        _selectedCardFolderId = _selectedCardFolderIds.FirstOrDefault() ?? "";
                }
                else
                {
                    _selectedCardFolderIds.Add(node.Id);
                    node.IsSelected = true;
                    _selectedCardFolderId = node.Id;
                }
            }
            // All Cards / UNASSIGNED roots stay single-select.
        }

        private void OnNewCard(object sender, RoutedEventArgs e)
        {
            var title = PromptForTitle("New Card", "Title:");
            if (string.IsNullOrWhiteSpace(title)) return;
            var id = StableIds.New();
            PushOrMergeWithReload(new CreateCardCommand(OpenConnection, id, title, SelectedCardFolderPath()), () =>
            {
                _selectedCardIds.Clear();
                _activeCardId = id;
                LoadContent();
                var created = _vm.Content.Cards.FirstOrDefault(c => c.Id == id);
                if (created != null)
                {
                    SelectTreeCard(created.Id, true);
                }
            });
        }

        private void OnNewCardFolder(object sender, RoutedEventArgs e)
        {
            var name = PromptForTitle("New Card Folder", "Folder name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            var parent = CardFolderTree.SelectedItem as CardFolderTreeNode;
            if (parent != null && parent.IsCard) parent = parent.Parent;
            var id = StableIds.New();
            PushOrMergeWithReload(new CreateCardFolderCommand(OpenConnection, id, name,
                parent == null || parent.IsAllCards ? null : parent.Id), () =>
            {
                _selectedCardFolderId = id;
                LoadContent();
                StatusText.Text = "Created card folder '" + name + "'.";
            });
        }

        private void OnRenameCardFolder(object sender, RoutedEventArgs e)
        {
            var folder = SelectedCardFolderNode();
            if (folder == null || folder.IsAllCards) return;
            var name = PromptForLibraryRename("Card Folder", folder.Name);
            if (name == null || string.Equals(name, folder.Name, StringComparison.Ordinal)) return;
            PushOrMergeWithReload(new RenameCardFolderCommand(OpenConnection, folder.Id, folder.Name, name), () =>
            {
                _selectedCardFolderId = folder.Id;
                LoadContent();
                StatusText.Text = "Renamed card folder to '" + name + "'.";
            });
        }

        private void OnDeleteCardFolder(object sender, RoutedEventArgs e)
        {
            var folder = SelectedCardFolderNode();
            if (folder == null || folder.IsAllCards) return;
            DeleteCardFolderWithChoice(folder);
        }

        /// <summary>Deletes a folder: empty folders confirm simply; non-empty
        /// folders choose between cascading contents or keeping the cards.</summary>
        private void DeleteCardFolderWithChoice(CardFolderTreeNode folder)
        {
            if (folder == null || folder.IsAllCards) return;

            var subfolderCount = 0;
            var cardCount = 0;
            CountFolderContents(folder, ref subfolderCount, ref cardCount);

            if (subfolderCount == 0 && cardCount == 0)
            {
                if (MessageBox.Show(this, "Delete empty card folder '" + folder.Name + "'?",
                    "Card Folders", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                PushOrMergeWithReload(new DeleteCardFolderCommand(OpenConnection, folder.Id), () =>
                {
                    _selectedCardFolderId = "";
                    LoadContent();
                    StatusText.Text = "Deleted card folder '" + folder.Name + "'.";
                });
                return;
            }

            var dialog = new CardFolderDeleteDialog(folder.Name, subfolderCount, cardCount) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.DeleteCards == null) return;
            var deleteCards = dialog.DeleteCards.Value;
            var name = folder.Name;
            PushOrMergeWithReload(new DeleteCardFolderTreeCommand(OpenConnection, folder.Id, deleteCards), () =>
            {
                _selectedCardFolderId = "";
                LoadContent();
                StatusText.Text = deleteCards
                    ? "Deleted folder '" + name + "' and its " + (cardCount == 1 ? "card" : cardCount + " cards") + "."
                    : "Deleted folder '" + name + "'; " + (cardCount == 1 ? "its card moved" : cardCount + " cards moved") + " to UNASSIGNED.";
            });
        }

        private void CountFolderContents(CardFolderTreeNode node, ref int subfolderCount, ref int cardCount)
        {
            foreach (var child in node.Children)
            {
                if (child.IsCard) cardCount++;
                else
                {
                    subfolderCount++;
                    CountFolderContents(child, ref subfolderCount, ref cardCount);
                }
            }
        }

        private void OnCardTreeRightClick(object sender, MouseButtonEventArgs e)
        {
            var item = FindCardFolderTreeItem(e.OriginalSource as DependencyObject);
            var node = item?.DataContext as CardFolderTreeNode;
            if (item == null || node == null) return;
            item.IsSelected = true;
            item.Focus();
            if (node.IsCard)
            {
                _activeCardId = node.Card.Id;
                if (!_selectedCardIds.Contains(node.Card.Id)) SelectSingleCard(node, false);
            }
            else SelectFolder(node);
        }

        /// <summary>Tailors the shared tree context menu to the node under the
        /// pointer: folders get Rename/Delete Folder, cards get Delete/Duplicate.</summary>
        private void OnCardTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var node = FindCardTreeNode(Mouse.DirectlyOver as DependencyObject);
            var isFolder = node != null && node.IsFolder;
            var isCard = node != null && node.IsCard;
            RenameCardFolderMenuItem.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
            DeleteCardFolderMenuItem.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
            CardTreeMenuSeparator.Visibility = isCard ? Visibility.Visible : Visibility.Collapsed;
            DeleteCardMenuItem.Visibility = isCard ? Visibility.Visible : Visibility.Collapsed;
            DuplicateCardMenuItem.Visibility = isCard ? Visibility.Visible : Visibility.Collapsed;
        }

        private static TreeViewItem FindCardFolderTreeItem(DependencyObject source)
        {
            while (source != null)
            {
                var item = source as TreeViewItem;
                if (item != null) return item;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private void OnCardTreeMouseDown(object sender, MouseButtonEventArgs e)
        {
            _cardTreeDragStart = e.GetPosition(CardFolderTree);
            _cardTreeDragInProgress = false;
            var node = FindCardTreeNode(e.OriginalSource as DependencyObject);
            if (node == null) return;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (node.IsCard)
            {
                if (shift)
                {
                    SelectCardRange(node);
                    // Never treat a shift-click as a drag start or editor open.
                    return;
                }
                // Explorer-style: plain-clicking an already-selected card keeps
                // the selection so a group drag can start from any member.
                if (ctrl) ToggleTreeSelection(node);
                else if (!_selectedCardIds.Contains(node.Card.Id)) SelectSingleCard(node, true);
                else _cardSelectionAnchorId = node.Card.Id; // clicked a selected card: re-anchor
                // Never handle the click here — the TreeViewItem still needs
                // it for native double-click (rename) and focus behavior.
            }
            else if (node.IsFolder || node.IsAllCards || node.IsUnassigned)
            {
                if (ctrl && node.IsFolder) ToggleTreeSelection(node);
                else if (!ctrl && !shift) SelectSingleFolder(node);
                // Shift+click on a folder is ignored (ranges span cards only);
                // Ctrl+click on a virtual root does nothing (single-select only).
            }
        }

        /// <summary>Explorer-style Shift+click: select the visual range from the
        /// anchor to the clicked card. The previous Shift-range (if any) is
        /// replaced; Ctrl-toggled members stay. First-ever Shift+click (no
        /// anchor) ranges from the first card.</summary>
        private void SelectCardRange(CardFolderTreeNode node)
        {
            if (node == null || !node.IsCard) return;
            var anchorIndex = 0;
            if (_cardSelectionAnchorId != null)
            {
                var knownAnchor = _cardVisualOrder.IndexOf(_cardSelectionAnchorId);
                if (knownAnchor >= 0) anchorIndex = knownAnchor;
            }
            var clickedIndex = _cardVisualOrder.IndexOf(node.Card.Id);
            if (clickedIndex < 0) return;

            var from = Math.Min(anchorIndex, clickedIndex);
            var to = Math.Max(anchorIndex, clickedIndex);

            // Drop the previous range's members (unless they were also
            // Ctrl-toggled in — those are respected and simply re-added below
            // if they fall inside the new range anyway).
            foreach (var id in _lastCardRangeIds)
            {
                if (!_selectedCardIds.Contains(id)) continue;
                _selectedCardIds.Remove(id);
                CardFolderTreeNode member;
                if (_cardTreeNodesByCardId.TryGetValue(id, out member)) member.IsSelected = false;
            }
            _lastCardRangeIds.Clear();

            for (var index = from; index <= to; index++)
            {
                var id = _cardVisualOrder[index];
                _lastCardRangeIds.Add(id);
                if (_selectedCardIds.Contains(id)) continue;
                _selectedCardIds.Add(id);
                CardFolderTreeNode member;
                if (_cardTreeNodesByCardId.TryGetValue(id, out member)) member.IsSelected = true;
            }
            _activeCardId = node.Card.Id;
            // The clicked card becomes the new anchor so the next Shift+click
            // ranges from here.
            _cardSelectionAnchorId = node.Card.Id;
        }

        private void OnCardTreeMouseMove(object sender, MouseEventArgs e)
        {
            if (_cardTreeDragInProgress || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(CardFolderTree);
            if (Math.Abs(point.X - _cardTreeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _cardTreeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var cardIds = _selectedCardIds.ToList();
            var folderIds = _selectedCardFolderIds.ToList();
            if (cardIds.Count == 0 && folderIds.Count == 0) return;
            _cardTreeDragInProgress = true;
            try
            {
                var payload = new DataObject();
                payload.SetData("TruthCardGame.CardIds", cardIds);
                payload.SetData("TruthCardGame.CardFolderIds", folderIds);
                DragDrop.DoDragDrop(CardFolderTree, payload, DragDropEffects.Move);
            }
            finally
            {
                _cardTreeDragInProgress = false;
            }
        }

        private void OnCardTreeDragOver(object sender, DragEventArgs e)
        {
            var node = FindCardFolderNode(e.OriginalSource as DependencyObject);
            var hasPayload = e.Data.GetDataPresent("TruthCardGame.CardIds") || e.Data.GetDataPresent("TruthCardGame.CardFolderIds");
            // A folder being dragged cannot drop onto itself.
            if (node != null && hasPayload &&
                e.Data.GetData("TruthCardGame.CardFolderIds") is IEnumerable<string> folders &&
                folders.Any(id => string.Equals(id, node.Id, StringComparison.OrdinalIgnoreCase)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = node != null && hasPayload ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnCardTreeDrop(object sender, DragEventArgs e)
        {
            var cardIds = (e.Data.GetData("TruthCardGame.CardIds") as IEnumerable<string>)
                ?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? new List<string>();
            var folderIds = (e.Data.GetData("TruthCardGame.CardFolderIds") as IEnumerable<string>)
                ?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? new List<string>();
            if (cardIds.Count == 0 && folderIds.Count == 0) return;
            var node = FindCardFolderNode(e.OriginalSource as DependencyObject);
            if (node == null) return;

            // Never drop a folder onto itself.
            if (folderIds.Any(id => string.Equals(id, node.Id, StringComparison.OrdinalIgnoreCase)))
            {
                e.Handled = true;
                return;
            }

            var destinationId = node.IsAllCards ? null : node.Id;
            var destinationName = node.Name;
            var movedCardCount = cardIds.Count;
            var movedFolderCount = folderIds.Count;
            PushOrMergeWithReload(new MoveCardSelectionCommand(OpenConnection, cardIds, folderIds, destinationId), () =>
            {
                ClearCardSelection();
                ClearFolderSelection();
                _activeCardId = null;
                _selectedCardFolderId = node.IsAllCards ? "" : node.Id;
                LoadContent();
                var what = movedCardCount + (movedCardCount == 1 ? " card" : " cards");
                if (movedFolderCount > 0) what += " and " + movedFolderCount + (movedFolderCount == 1 ? " folder" : " folders");
                StatusText.Text = "Moved " + what + " to '" + destinationName + "'.";
            });
            e.Handled = true;
        }

        private static CardFolderTreeNode FindCardFolderNode(DependencyObject source)
        {
            while (source != null)
            {
                var item = source as TreeViewItem;
                if (item != null)
                {
                    var node = item.DataContext as CardFolderTreeNode;
                    return node != null && !node.IsCard ? node : null;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private static CardFolderTreeNode FindCardTreeNode(DependencyObject source)
        {
            while (source != null)
            {
                var item = source as TreeViewItem;
                if (item != null) return item.DataContext as CardFolderTreeNode;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private void SelectTreeCard(string cardId, bool openEditor)
        {
            CardFolderTreeNode node;
            if (_cardTreeNodesByCardId.TryGetValue(cardId ?? "", out node)) SelectSingleCard(node, openEditor);
        }

        private void OnCardTreeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var node = FindCardTreeNode(e.OriginalSource as DependencyObject);
            if (node == null || !node.IsCard) return;
            var newTitle = PromptForLibraryRename("Card", node.Card.Title);
            if (newTitle == null || string.Equals(newTitle, node.Card.Title, StringComparison.Ordinal)) return;
            PushOrMergeWithReload(new RenameCardCommand(OpenConnection, node.Card.Id, node.Card.Title ?? "", newTitle), () =>
            {
                node.Card.Title = newTitle;
                if (_cardBuffer?.CardId == node.Card.Id)
                {
                    _cardBuffer.Title = newTitle;
                    SyncCardEditorFromBuffer();
                }
                _activeCardId = node.Card.Id;
                _selectedCardIds.Clear();
                _selectedCardIds.Add(node.Card.Id);
                LoadContent();
                StatusText.Text = "Renamed card to '" + newTitle + "'.";
            });
        }

        private void OnDeleteCard(object sender, RoutedEventArgs e)
        {
            DeleteSelectedCardsAndFolders();
        }

        /// <summary>Batch delete: explicitly selected cards always delete; selected
        /// folders get the cascade/keep choice (one dialog for the whole batch).</summary>
        private void DeleteSelectedCardsAndFolders()
        {
            var cardIds = _selectedCardIds.ToList();
            var folderIds = _selectedCardFolderIds.ToList();

            // Single-folder path keeps the tailored dialogs.
            if (cardIds.Count == 0 && folderIds.Count == 1)
            {
                CardFolderTreeNode folder;
                if (_cardFolderNodesById.TryGetValue(folderIds[0], out folder))
                    DeleteCardFolderWithChoice(folder);
                return;
            }
            if (cardIds.Count == 0 && folderIds.Count == 0)
            {
                // Legacy fallback: the folder shown as selected in the tree.
                var folder = SelectedCardFolderNode();
                if (folder != null && !folder.IsAllCards) DeleteCardFolderWithChoice(folder);
                return;
            }
            if (cardIds.Count == 1 && folderIds.Count == 0)
            {
                var card = _vm.Content.Cards.FirstOrDefault(candidate => candidate.Id == cardIds[0]);
                if (card == null) return;
                if (MessageBox.Show(this, $"Delete card '{card.Title}'? Only card-owned data is removed.",
                    "Cards", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                PushOrMergeWithReload(new DeleteCardCommand(OpenConnection, card.Id), () =>
                {
                    _cardBuffer = null;
                    CardEditorInspectorPanel.Visibility = Visibility.Collapsed;
                    CardActionSequenceHost.Content = null;
                    ClearCardSelection();
                    ClearFolderSelection();
                    _activeCardId = null;
                    LoadContent();
                });
                return;
            }

            // Mixed batch: one confirmation, one choice for all folders.
            var totalCards = cardIds.Count;
            var folderCardCounts = new List<int>();
            foreach (var folderId in folderIds)
            {
                CardFolderTreeNode folder;
                if (_cardFolderNodesById.TryGetValue(folderId, out folder))
                {
                    var subfolders = 0;
                    var cards = 0;
                    CountFolderContents(folder, ref subfolders, ref cards);
                    totalCards += cards;
                }
            }
            bool deleteFolderCards;
            if (folderIds.Count > 0)
            {
                var dialog = new CardFolderDeleteDialog(null, folderIds.Count, totalCards) { Owner = this };
                if (dialog.ShowDialog() != true || dialog.DeleteCards == null) return;
                deleteFolderCards = dialog.DeleteCards.Value;
            }
            else
            {
                if (MessageBox.Show(this, $"Delete {cardIds.Count} cards? Only card-owned data is removed.",
                    "Cards", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                deleteFolderCards = false;
            }

            PushOrMergeWithReload(new DeleteCardSelectionCommand(OpenConnection, cardIds, folderIds, deleteFolderCards), () =>
            {
                _cardBuffer = null;
                CardEditorInspectorPanel.Visibility = Visibility.Collapsed;
                CardActionSequenceHost.Content = null;
                ClearCardSelection();
                ClearFolderSelection();
                _activeCardId = null;
                _selectedCardFolderId = "";
                LoadContent();
                StatusText.Text = "Deleted " + cardIds.Count + (cardIds.Count == 1 ? " card" : " cards")
                    + (folderIds.Count > 0 ? " and " + folderIds.Count + (folderIds.Count == 1 ? " folder" : " folders") + " (cards " + (deleteFolderCards ? "deleted" : "kept") + ")" : "") + ".";
            });
        }

        private void OnDuplicateCard(object sender, RoutedEventArgs e)
        {
            var cardIds = _selectedCardIds.ToList();
            var folderIds = _selectedCardFolderIds.ToList();

            // Single-card path keeps the existing behavior.
            if (folderIds.Count == 0 && cardIds.Count <= 1)
            {
                var card = _vm.Content.Cards.FirstOrDefault(candidate => candidate.Id == _activeCardId);
                if (card == null && cardIds.Count == 1)
                    card = _vm.Content.Cards.FirstOrDefault(candidate => candidate.Id == cardIds[0]);
                if (card == null) return;
                var id = StableIds.New();
                PushOrMergeWithReload(new DuplicateCardCommand(OpenConnection, card.Id, id, card.Title + " (copy)"), () =>
                {
                    ClearCardSelection();
                    _activeCardId = id;
                    LoadContent();
                    var created = _vm.Content.Cards.FirstOrDefault(c => c.Id == id);
                    if (created != null)
                    {
                        SelectTreeCard(created.Id, true);
                    }
                });
                return;
            }

            // Batch: duplicate everything, then select the copies.
            var command = new DuplicateCardSelectionCommand(OpenConnection, cardIds, folderIds);
            PushOrMergeWithReload(command, () =>
            {
                var newCardIds = command.NewCardIds.ToList();
                var newFolderIds = command.NewFolderIds.ToList();
                ClearCardSelection();
                ClearFolderSelection();
                _activeCardId = null;
                LoadContent();
                // Deselect originals happened above; select the duplicates.
                foreach (var newId in newCardIds) _selectedCardIds.Add(newId);
                foreach (var newId in newFolderIds) _selectedCardFolderIds.Add(newId);
                var firstNewFolder = newFolderIds.FirstOrDefault();
                _selectedCardFolderId = firstNewFolder ?? "";
                var firstNewCard = newCardIds.FirstOrDefault();
                if (firstNewCard != null)
                {
                    _activeCardId = firstNewCard;
                    var created = _vm.Content.Cards.FirstOrDefault(c => c.Id == firstNewCard);
                    if (created != null) SelectTreeCard(created.Id, true);
                }
                LoadContent(); // rebuild so the new selection state paints
                StatusText.Text = "Duplicated " + newCardIds.Count + (newCardIds.Count == 1 ? " card" : " cards")
                    + (newFolderIds.Count > 0 ? " in " + newFolderIds.Count + (newFolderIds.Count == 1 ? " folder" : " folders") : "") + ".";
            });
        }

        // ---------- buffered card editor (Inspector) ----------

        /// <summary>
        /// Opens the buffered editor for a card. A dirty buffer for a DIFFERENT
        /// card prompts Save / Discard / Cancel first.
        /// </summary>
        private void OpenCardEditor(CardDefinition card)
        {
            if (_cardBuffer != null && _cardBuffer.CardId != card.Id)
            {
                if (!ConfirmLeavingDirtyCard("switch to '" + card.Title + "'")) return;
            }

            _cardBuffer = new CardEditBuffer(card);
            SyncCardEditorFromBuffer();
            CardEditorInspectorPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Closes the buffered editor. Prompts when the buffer is dirty unless
        /// the caller already handled it (or discards deliberately, e.g. Delete).
        /// Returns false when the user canceled and the caller should abort.
        /// </summary>
        private bool CloseCardEditor(bool promptForDirty)
        {
            if (_cardBuffer != null && promptForDirty && _cardBuffer.IsDirty)
            {
                if (!ConfirmLeavingDirtyCard("stop editing this card")) return false;
            }
            _cardBuffer = null;
            CardEditorInspectorPanel.Visibility = Visibility.Collapsed;
            CardActionSequenceHost.Content = null;
            return true;
        }

        /// <summary>Save / Discard / Cancel for a dirty buffer. Returns false only on Cancel.</summary>
        private bool ConfirmLeavingDirtyCard(string whatNext)
        {
            if (_cardBuffer == null || !_cardBuffer.IsDirty) return true;

            var choice = MessageBox.Show(this,
                "This card has unsaved changes.\n\nSave them before you " + whatNext + "?",
                "Unsaved Card Changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            switch (choice)
            {
                case MessageBoxResult.Yes:
                    return SaveCardBuffer();
                case MessageBoxResult.No:
                    return true; // discard
                default:
                    return false; // cancel: stay on this card
            }
        }

        /// <summary>Pushes every buffered change as ONE composite undo step.</summary>
        private bool SaveCardBuffer()
        {
            if (_cardBuffer == null) return true;
            var card = _vm.Content.Cards.FirstOrDefault(c => c.Id == _cardBuffer.CardId);
            if (card == null)
            {
                _cardBuffer = null;
                return true;
            }

            var commands = new List<IAuthoringCommand>();
            if (!string.Equals(_cardBuffer.Title, card.Title ?? "", StringComparison.Ordinal))
            {
                commands.Add(new RenameCardCommand(OpenConnection, card.Id, card.Title ?? "", _cardBuffer.Title));
            }
            if (!string.Equals(_cardBuffer.BodyText, card.BodyText ?? "", StringComparison.Ordinal))
            {
                commands.Add(new SetCardBodyCommand(OpenConnection, card.Id, card.BodyText ?? "", _cardBuffer.BodyText));
            }
            if (!string.Equals(CardRepository.NormalizeFolder(_cardBuffer.FolderPath),
                CardRepository.NormalizeFolder(card.FolderPath), StringComparison.Ordinal))
            {
                commands.Add(new SetCardFolderCommand(OpenConnection, card.Id, card.FolderPath, _cardBuffer.FolderPath));
            }
            if (_cardBuffer.FieldsChanged)
            {
                commands.Add(new SetCardRelationsCommand(OpenConnection, card,
                    card.CardTagIds, card.KinkIds, card.RequiredEquipmentIds, card.RequiredCapabilityIds,
                    _cardBuffer.CardTagIds, _cardBuffer.KinkIds, _cardBuffer.RequiredEquipmentIds, _cardBuffer.RequiredCapabilityIds));
            }
            if (_cardBuffer.SequenceChanged)
            {
                commands.Add(new ReplaceCardSequenceCommand(OpenConnection, card.Id,
                    _cardBuffer.OriginalSequence, _cardBuffer.Sequence));
            }

            if (commands.Count == 0) return true; // nothing changed; nothing to save

            var composite = commands.Count == 1
                ? commands[0]
                : new CompositeCommand("Edit card", commands.ToArray());

            try
            {
                _stack.PushOrMerge(composite);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Persistence error";
                MessageBox.Show(this, "Database write failed:\n\n" + ex.Message,
                    "Workbench", MessageBoxButton.OK, MessageBoxImage.Error);
                ReloadAllFromDb();
                return false;
            }

            // Update the in-memory definition to match what was saved.
            card.Title = _cardBuffer.Title;
            card.BodyText = _cardBuffer.BodyText;
            card.FolderPath = CardRepository.NormalizeFolder(_cardBuffer.FolderPath);
            card.CardTagIds = new List<string>(_cardBuffer.CardTagIds);
            card.KinkIds = new List<string>(_cardBuffer.KinkIds);
            card.RequiredEquipmentIds = new List<string>(_cardBuffer.RequiredEquipmentIds);
            card.RequiredCapabilityIds = new List<string>(_cardBuffer.RequiredCapabilityIds);
            card.Sequence = _cardBuffer.Sequence;

            // Fresh buffer over the just-saved state: dirty flag clears.
            _cardBuffer = new CardEditBuffer(card);
            SyncCardEditorFromBuffer();
            BindCardList();
            SelectTreeCard(card.Id, false);
            StatusText.Text = $"Saved card '{card.Title}' (one undo step).";
            return true;
        }

        private void OnCardSave(object sender, RoutedEventArgs e)
        {
            SaveCardBuffer();
        }

        private void OnCardRevert(object sender, RoutedEventArgs e)
        {
            if (_cardBuffer == null) return;
            if (_cardBuffer.IsDirty &&
                MessageBox.Show(this, "Discard all unsaved changes to this card?",
                    "Revert Card", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
            var card = _vm.Content.Cards.FirstOrDefault(c => c.Id == _cardBuffer.CardId);
            if (card != null)
            {
                _cardBuffer = new CardEditBuffer(card);
                SyncCardEditorFromBuffer();
                StatusText.Text = "Reverted unsaved card changes.";
            }
        }

        /// <summary>Rebuilds the inspector fields + pickers + sequence host from the buffer.</summary>
        private void SyncCardEditorFromBuffer()
        {
            if (_cardBuffer == null) return;
            _syncingCardEditor = true;
            try
            {
                var card = _vm.Content.Cards.FirstOrDefault(c => c.Id == _cardBuffer.CardId);
                var title = _cardBuffer.Title;
                CardEditorTitle.Text = "Card" + (string.IsNullOrEmpty(title) ? "" : " — " + title);
                CardTitleBox.Text = title;
                CardBodyBox.Text = _cardBuffer.BodyText;
                CardFolderBox.Text = _cardBuffer.FolderPath;

                CardTagPicker.SetItems(_vm.Content.CardTagDefinitions.Select(t => new RelationChoice { Id = t.Id, DisplayName = t.Title }), _cardBuffer.CardTagIds);
                CardKinkPicker.SetItems(_vm.Content.KinkDefinitions.Select(k => new RelationChoice { Id = k.Id, DisplayName = k.Title }), _cardBuffer.KinkIds);
                CardEquipmentPicker.SetItems(_vm.Content.EquipmentDefinitions.Select(x => new RelationChoice { Id = x.Id, DisplayName = x.Title }), _cardBuffer.RequiredEquipmentIds);
                CardCapabilityPicker.SetItems(_vm.Content.SmartToyCapabilityDefinitions.Select(x => new RelationChoice { Id = x.Id, DisplayName = x.Title }), _cardBuffer.RequiredCapabilityIds);

                BuildCardSequenceHost();
                UpdateCardDirtyText();
            }
            finally
            {
                _syncingCardEditor = false;
            }
        }

        /// <summary>Builds the buffered sequence's editor host, subscribing row edits to the buffer.</summary>
        private void BuildCardSequenceHost()
        {
            var card = _vm.Content.Cards.FirstOrDefault(c => c.Id == _cardBuffer?.CardId);
            if (card == null || _cardBuffer == null)
            {
                CardActionSequenceHost.Content = null;
                return;
            }

            var sequenceEditor = CardEditorSequenceHost.Build(_cardBuffer.Sequence, card.Id, _cardBuffer.Title, _vm.Content);
            SubscribeBufferedRows(sequenceEditor);
            CardActionSequenceHost.Content = sequenceEditor;
        }

        private void SubscribeBufferedRows(ActionSequenceEditorViewModel sequence)
        {
            if (sequence == null) return;
            foreach (var row in sequence.Rows)
            {
                row.PropertyChanged += (_, args) =>
                {
                    if (_syncingCardEditor) return;
                    if (args.PropertyName != nameof(ActionRowData.TextValue) &&
                        args.PropertyName != nameof(ActionRowData.NumberText) &&
                        args.PropertyName != nameof(ActionRowData.SecondaryNumberText) &&
                        args.PropertyName != nameof(ActionRowData.PatternValue) &&
                        args.PropertyName != nameof(ActionRowData.IsBlocking)) return;
                    UpdateCardDirtyText();
                };
                foreach (var option in row.PromptOptions)
                {
                    option.PropertyChanged += (_, _) => UpdateCardDirtyText();
                    SubscribeBufferedRows(option.ActionSequence);
                }
            }
        }

        private void UpdateCardDirtyText()
        {
            var dirty = _cardBuffer != null && _cardBuffer.IsDirty;
            CardDirtyText.Text = dirty ? "● unsaved changes" : "";
            CardSaveButton.IsEnabled = true;
            CardRevertButton.IsEnabled = true;
            CardDirtyText.Text = dirty
                ? "Unsaved Card changes — session follow will pause before another Card executes."
                : "";
            if (dirty)
            {
                CardSaveButton.Background = new SolidColorBrush(Color.FromRgb(255, 200, 87));
                CardSaveButton.Foreground = new SolidColorBrush(Color.FromRgb(27, 27, 28));
                CardSaveButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 224, 138));
                CardSaveButton.BorderThickness = new Thickness(2);
                CardSaveButton.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                CardSaveButton.ClearValue(Button.BackgroundProperty);
                CardSaveButton.ClearValue(Button.ForegroundProperty);
                CardSaveButton.ClearValue(Button.BorderBrushProperty);
                CardSaveButton.ClearValue(Button.BorderThicknessProperty);
                CardSaveButton.ClearValue(Button.FontWeightProperty);
            }
        }

        /// <summary>Title/body keystrokes land in the buffer only.</summary>
        private void OnCardBufferFieldChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingCardEditor || _cardBuffer == null) return;
            _cardBuffer.Title = CardTitleBox.Text;
            _cardBuffer.BodyText = CardBodyBox.Text;
            _cardBuffer.FolderPath = CardFolderBox.Text;
            CardEditorTitle.Text = "Card" + (string.IsNullOrEmpty(_cardBuffer.Title) ? "" : " — " + _cardBuffer.Title);
            UpdateCardDirtyText();
        }

        /// <summary>Relation picker selections land in the buffer only.</summary>
        private void OnCardBufferRelationsChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingCardEditor || _cardBuffer == null) return;

            _cardBuffer.CardTagIds.Clear();
            _cardBuffer.CardTagIds.AddRange(CardTagPicker.SelectedIds);
            _cardBuffer.KinkIds.Clear();
            _cardBuffer.KinkIds.AddRange(CardKinkPicker.SelectedIds);
            _cardBuffer.RequiredEquipmentIds.Clear();
            _cardBuffer.RequiredEquipmentIds.AddRange(CardEquipmentPicker.SelectedIds);
            _cardBuffer.RequiredCapabilityIds.Clear();
            _cardBuffer.RequiredCapabilityIds.AddRange(CardCapabilityPicker.SelectedIds);
            UpdateCardDirtyText();
        }

        /// <summary>Adds a default instance of the picked action type to the card buffer and refreshes the rows.</summary>
        private void AddBufferedCardAction(ActionSequenceEditorViewModel sequence, string typeKey)
        {
            if (_cardBuffer == null) return;
            _cardBuffer.AddAction(_cardBuffer.FindSequence(sequence.SequenceId) ?? _cardBuffer.Sequence,
                typeKey, id => sequence.CreateDefaultInstance(typeKey, id));
            RebuildCardSequenceHost();
            StatusText.Text = "Added " + ActionTypeRegistry.ByTypeKey(typeKey).DisplayLabel +
                " to the card (unsaved — Save Card applies it).";
        }

        /// <summary>Rebuilds the card sequence host after a buffer row operation, keeping row-edit subscriptions.</summary>
        private void RebuildCardSequenceHost()
        {
            if (_cardBuffer == null)
            {
                CardActionSequenceHost.Content = null;
                return;
            }
            BuildCardSequenceHost();
            UpdateCardDirtyText();
        }

        // ---------- Ticket 14: session weighting editors ----------

        private void SyncSessionWeightingPanel()
        {
            if (_vm.SelectedSession == null) return;
            _syncingWeighting = true;
            try
            {
                var weighting = _vm.SelectedSession.CardWeighting ?? new SessionCardWeightingDefinition();
                LoveBaseBox.Text = FormatWeight(weighting.LoveBase);
                LoveGainBox.Text = FormatWeight(weighting.LoveHappinessGain);
                LikeBaseBox.Text = FormatWeight(weighting.LikeBase);
                LikeGainBox.Text = FormatWeight(weighting.LikeHappinessGain);
                TortureBaseBox.Text = FormatWeight(weighting.TortureBase);
                TortureUnhappinessBox.Text = FormatWeight(weighting.TortureUnhappinessGain);
            }
            finally
            {
                _syncingWeighting = false;
            }
        }

        private static string FormatWeight(float value) => value.ToString("0.##");

        private void OnSessionWeightingChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingWeighting || _vm.SelectedSession == null) return;

            if (!TryParseWeight(LoveBaseBox.Text, out var lb) ||
                !TryParseWeight(LoveGainBox.Text, out var lg) ||
                !TryParseWeight(LikeBaseBox.Text, out var kb) ||
                !TryParseWeight(LikeGainBox.Text, out var kg) ||
                !TryParseWeight(TortureBaseBox.Text, out var tb) ||
                !TryParseWeight(TortureUnhappinessBox.Text, out var tg))
            {
                return; // incomplete edit; wait for valid numbers
            }

            var old = _vm.SelectedSession.CardWeighting ?? new SessionCardWeightingDefinition();
            var updated = new SessionCardWeightingDefinition
            {
                LoveBase = lb, LoveHappinessGain = lg,
                LikeBase = kb, LikeHappinessGain = kg,
                TortureBase = tb, TortureUnhappinessGain = tg,
            };

            if (Math.Abs(old.LoveBase - lb) < 0.0001f && Math.Abs(old.LoveHappinessGain - lg) < 0.0001f &&
                Math.Abs(old.LikeBase - kb) < 0.0001f && Math.Abs(old.LikeHappinessGain - kg) < 0.0001f &&
                Math.Abs(old.TortureBase - tb) < 0.0001f && Math.Abs(old.TortureUnhappinessGain - tg) < 0.0001f)
            {
                return;
            }

            PushOrMergeWithReload(new SetSessionWeightingCommand(OpenConnection, _vm.SelectedSession.Id, old, updated),
                () => _vm.SelectedSession.CardWeighting = updated);
        }

        private static bool TryParseWeight(string text, out float value)
        {
            return float.TryParse((text ?? "").Trim(), System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 0f;
        }

        // ---------- Ticket 15: selection diagnostics ----------

        private void OnPreviewEligibleCards(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) return;
            List<string> lines;
            try
            {
                lines = EvaluateSelectionLines(_vm.SelectedPhase, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Selection diagnostics blocked: " + ex.Message,
                    "Selection diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            MessageBox.Show(this, string.Join("\n", lines), "Eligible Cards — " + _vm.SelectedPhase.Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnShowSelectionDiagnostics(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) return;
            // Weighting context: prefer the session that's placing this phase,
            // else the selected session, else defaults.
            try
            {
                var weighting = _vm.SelectedSession?.CardWeighting;
                var window = new SelectionDiagnosticsWindow(_vm.Content, _vm.SelectedPhase, LoadProfileSnapshot(), weighting);
                window.Owner = this;
                window.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Selection diagnostics blocked: " + ex.Message,
                    "Selection diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<string> EvaluateSelectionLines(PhaseDefinition phase, float? happinessOverride)
        {
            var profile = CardSelectionProfile.FromProfile(LoadProfileSnapshot());
            var happiness = happinessOverride ?? 50f;
            var allTitles = phase.MustHaveAllCardTags.Select(id => SelectionDiagnosticsFormatter.CardTag(_vm.Content, id)).ToList();
            var anyTitles = phase.MustHaveAnyCardTags.Select(id => SelectionDiagnosticsFormatter.CardTag(_vm.Content, id)).ToList();
            var lines = new List<string>
            {
                $"Phase: {SelectionDiagnosticsFormatter.Phase(phase)}  ·  Happiness: {happiness:0.#}",
                $"Query: ALL [{string.Join(", ", allTitles)}]  ANY [{string.Join(", ", anyTitles)}]",
                "",
            };
            foreach (var card in _vm.Content.Cards)
            {
                var eligibility = CardEligibilityEngine.EvaluateOne(card, phase, profile);
                if (eligibility.IsEligible)
                {
                    var weight = CardWeightCalculator.ComputeWeight(card, profile,
                        _vm.SelectedSession?.CardWeighting ?? new SessionCardWeightingDefinition(), happiness);
                    lines.Add($"✓ {SelectionDiagnosticsFormatter.Card(card)}  (weight {weight:0.###})");
                }
                else
                {
                    lines.Add($"× {SelectionDiagnosticsFormatter.Card(card)}  — {string.Join("; ", eligibility.Reasons.Select(r => SelectionDiagnosticsFormatter.Rejection(_vm.Content, r)))}");
                }
            }
            return lines;
        }

        private UserProfileSnapshot LoadProfileSnapshot()
        {
            return UserProfileSelectionLoader.LoadSnapshot();
        }

        // ---------- Ticket 16: consumer-style session start ----------

        private void OnPlayByType(object sender, RoutedEventArgs e)
        {
            if (!TryGetRunSeed(out var seed)) return;
            var content = _vm.Content;
            var catalog = new ContentCatalog(content);
            UserProfileSnapshot profileSnapshot;
            try
            {
                profileSnapshot = LoadProfileSnapshot();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Session start blocked: profile error";
                MessageBox.Show(this, "Session start blocked because the existing UserProfile database could not be read:\n\n" + ex.Message,
                    "User profile error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var profile = CardSelectionProfile.FromProfile(profileSnapshot);
            var eligibility = new SessionTypeEligibility(catalog);

            var lines = new List<string>();
            var validTypes = new List<SessionTypeDefinition>();
            foreach (var type in content.SessionTypes)
            {
                var result = eligibility.Evaluate(type, profile);
                if (result.IsEligible)
                {
                    var count = content.Sessions.Count(s => s.SessionTypeId == type.Id);
                    lines.Add($"{SelectionDiagnosticsFormatter.SessionType(type)} — {count} session(s)");
                    if (count > 0) validTypes.Add(type);
                }
                else
                {
                    lines.Add($"{SelectionDiagnosticsFormatter.SessionType(type)} — ineligible (missing: {SelectionDiagnosticsFormatter.Capabilities(content, result.MissingCapabilityIds)})");
                }
            }

            if (validTypes.Count == 0)
            {
                MessageBox.Show(this, "No eligible session types with sessions:\n\n" + string.Join("\n", lines),
                    "Play by Type", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var choice = new SessionTypePickerWindow(lines, validTypes.Select(t => t.Title).ToList());
            choice.Owner = this;
            if (choice.ShowDialog() != true || string.IsNullOrEmpty(choice.SelectedTitle)) return;

            var selectedType = validTypes.FirstOrDefault(t => t.Title == choice.SelectedTitle);
            if (selectedType == null) return;

            // Uniform selection with a dedicated RNG.
            if (!eligibility.TrySelectSession(selectedType.Id, profile,
                SeededRandomDomains.CreateSessionSelection(seed), out var session))
            {
                MessageBox.Show(this, "No session of that type is available.", "Play by Type",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var player = new ReferencePlayerWindow { Seed = seed };
            player.Owner = this;
            try
            {
                player.RunSession(session.Id);
                player.Show();
                StatusText.Text = $"Playing '{SelectionDiagnosticsFormatter.Session(session)}' (type '{SelectionDiagnosticsFormatter.SessionType(selectedType)}') — spawned with Happiness 50.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to start session:\n\n" + ex.Message, "Play by Type",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Refreshes the SessionType combo after catalog edits.</summary>
        private void BindSessionTypeBox()
        {
            var selected = SessionTypeBox.SelectedItem as SessionTypeDefinition;
            SessionTypeBox.ItemsSource = _vm.Content.SessionTypes.OrderBy(t => t.SortOrder).ToList();
            if (selected != null)
            {
                SessionTypeBox.SelectedItem = _vm.Content.SessionTypes.FirstOrDefault(t => t.Id == selected.Id);
            }
        }

        private static string TitleOf<T>(IEnumerable<T> definitions, string id) where T : class
        {
            if (definitions == null) return id;
            var definition = definitions.FirstOrDefault(item =>
            {
                if (item is CardTagDefinition tag) return tag.Id == id;
                return false;
            });
            return definition is CardTagDefinition cardTag ? cardTag.Title : id;
        }
    }

    /// <summary>
    /// Adapter for the Card editor's ActionSequence host: builds the
    /// GraphNodeViewModel + ActionSequenceEditorViewModel pair exactly like a
    /// Phase action node does (the node VM is a data holder, not a canvas node).
    /// Two overloads: from a card definition (display) or from the edit
    /// buffer's working sequence (the editing path).
    /// </summary>
    public static class CardEditorSequenceHost
    {
        public static ActionSequenceEditorViewModel Build(CardDefinition card, GameContentDefinition content)
        {
            return Build(card.Sequence, card.Id, card.Title, content);
        }

        public static ActionSequenceEditorViewModel Build(ActionSequenceDefinition sequence, string cardId,
            string cardTitle, GameContentDefinition content)
        {
            var node = new GraphNodeViewModel
            {
                Id = cardId,
                Title = cardTitle,
                Kind = "card",
                CanEditActions = true,
            };
            var temperatures = content.Temperatures
                .Select(t => new ActionParameterOption { Id = t.Id, Name = t.Title })
                .ToList();
            var stats = ConfiguredActionParameters.StatOptions(content);
            var resources = content.Resources
                .Where(r => string.Equals(r.Kind, ResourceKinds.Cutscene, StringComparison.OrdinalIgnoreCase))
                .Select(r => new ActionParameterOption { Id = r.Id, Name = string.IsNullOrEmpty(r.Name) ? r.Id : r.Name })
                .ToList();
            var toyPatterns = content.Resources
                .Where(r => string.Equals(r.Kind, ResourceKinds.ToyPattern, StringComparison.OrdinalIgnoreCase))
                .Select(r => new ActionParameterOption { Id = r.Id, Name = string.IsNullOrEmpty(r.Name) ? r.Id : r.Name })
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var dialogTags = content.DialogTags
                .Select(t => new RelationChoice { Id = t.Id, DisplayName = string.IsNullOrEmpty(t.Title) ? t.Id : t.Title })
                .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var toyCapabilities = content.SmartToyCapabilityDefinitions
                .Select(c => new ActionParameterOption { Id = c.Id, Name = string.IsNullOrEmpty(c.Title) ? c.Id : c.Title })
                .ToList();
            var performanceEvents = content.PerformanceEvents
                .Select(e => new ActionParameterOption { Id = e.Id, Name = string.IsNullOrEmpty(e.Name) ? e.Id : e.Name })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            node.ActionSequence = new ActionSequenceEditorViewModel(node, sequence?.Id,
                ActionOwnerScope.CardSequence, sequence?.Instances,
                temperatures, resources, new List<ExitOption>(), statOptions: stats,
                toyCapabilityOptions: toyCapabilities, toyPatternOptions: toyPatterns,
                dialogTagOptions: dialogTags, performanceEventOptions: performanceEvents);
            return node.ActionSequence;
        }
    }
}
