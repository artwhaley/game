using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private UserProfileWindow _profileWindow;
        private CardEditBuffer _cardBuffer;

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

        private static readonly string[] MilestoneBCatalogKinds = { "Session Types", "Card Tags", "Kinks", "Equipment", "Smart Toys" };

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
            CatalogEntryList.DisplayMemberPath = nameof(CardTagDefinition.Title);
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
            }
            if (selectedId != null) SelectCatalogEntry(selectedId);
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
                CatalogDescriptionBox.Visibility = entry is KinkDefinition ? Visibility.Visible : Visibility.Collapsed;
                CatalogCategoryBox.Visibility = entry is EquipmentDefinition || entry is SmartToyCapabilityDefinition
                    ? Visibility.Visible : Visibility.Collapsed;
                CatalogCapabilityPicker.Visibility = entry is SessionTypeDefinition ? Visibility.Visible : Visibility.Collapsed;
                CatalogDescriptionBox.Text = (entry as KinkDefinition)?.Description ?? "";
                CatalogCategoryBox.Text = (entry as EquipmentDefinition)?.Category
                    ?? (entry as SmartToyCapabilityDefinition)?.Category ?? "";
                CatalogCapabilityPicker.SetItems(
                    _vm.Content.SmartToyCapabilityDefinitions.Select(capability => new RelationChoice
                    {
                        Id = capability.Id, DisplayName = capability.Title
                    }),
                    (entry as SessionTypeDefinition)?.RequiredCapabilityIds ?? Enumerable.Empty<string>());
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
                SortOrder = oldValue.SortOrder, RequiredCapabilityIds = CatalogCapabilityPicker.SelectedIds.ToList()
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
                        var count = WithConnectionResult(connection => CatalogRepositories.CountSmartToyCapabilityUsage(connection, capability.Id));
                        text = $"Required by {count} card(s)/session type(s).";
                    }
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
                    CatalogEntryList.Items[i] is SmartToyCapabilityDefinition capability && capability.Id == id)
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
                default: throw new InvalidOperationException("Unknown catalog kind '" + uiKind + "'.");
            }
        }

        private void OnDeleteCatalogEntry(object sender, RoutedEventArgs e)
        {
            var kind = SelectedCatalogKind;
            string id;
            string title;
            int usage;

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
                    usage = WithConnectionResult(connection => CatalogRepositories.CountSmartToyCapabilityUsage(connection, id));
                    break;
                default:
                    return;
            }

            if (usage > 0)
            {
                MessageBox.Show(this,
                    $"'{title}' is referenced by {usage} item(s) and cannot be deleted.\n\nRemove the references first.",
                    "Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(this, $"Delete '{title}'?", "Catalog",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var kindKey = CatalogKindOf(kind);
            PushOrMergeWithReload(new DeleteCatalogEntryCommand(OpenConnection, kindKey, id, title), () =>
            {
                RemoveCatalogEntryFromContent(kindKey, id);
                BindCatalogEntries();
                BindSessionTypeBox();
                StatusText.Text = $"Deleted '{title}'.";
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
            // Remember the selection across rebinds (filter typing must not
            // silently close a dirty card editor).
            var selectedId = (CardList.SelectedItem as CardDefinition)?.Id;

            var query = (CardFilter.Text ?? "").Trim();
            var cards = string.IsNullOrEmpty(query)
                ? _vm.Content.Cards.ToList()
                : _vm.Content.Cards.Where(c => (c.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            CardList.ItemsSource = cards;
            CardList.DisplayMemberPath = nameof(CardDefinition.Title);

            if (selectedId != null)
            {
                var restore = cards.FirstOrDefault(c => c.Id == selectedId);
                if (restore != null) CardList.SelectedItem = restore;
            }
        }

        private void OnCardFilterChanged(object sender, TextChangedEventArgs e)
        {
            // Preserve the open editor during filter rebinds: the selection
            // restore in BindCardList keeps the same card selected, so this
            // path never fires a null-selection close while typing.
            BindCardList();
        }

        private void OnCardListChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CardList.SelectedItem is CardDefinition card)
            {
                // Same card re-selected (filter rebind restore) — no-op.
                if (_cardBuffer != null && _cardBuffer.CardId == card.Id) return;
                OpenCardEditor(card);
            }
            // Null selection (list emptied by filter) keeps the editor open so
            // unsaved work survives the keystroke; the editor closes through
            // explicit paths (card switch, library tab switch, app close).
        }

        private void OnNewCard(object sender, RoutedEventArgs e)
        {
            var title = PromptForTitle("New Card", "Title:");
            if (string.IsNullOrWhiteSpace(title)) return;
            var id = StableIds.New();
            PushOrMergeWithReload(new CreateCardCommand(OpenConnection, id, title), () =>
            {
                LoadContent();
                var created = _vm.Content.Cards.FirstOrDefault(c => c.Id == id);
                if (created != null)
                {
                    CardList.SelectedItem = created;
                    OpenCardEditor(created);
                }
            });
        }

        private void OnDeleteCard(object sender, RoutedEventArgs e)
        {
            if (!(CardList.SelectedItem is CardDefinition card)) return;
            if (MessageBox.Show(this, $"Delete card '{card.Title}'? Only card-owned data is removed.",
                "Cards", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            PushOrMergeWithReload(new DeleteCardCommand(OpenConnection, card.Id), () =>
            {
                _cardBuffer = null; // deleting the buffered card discards the buffer with it
                CardEditorInspectorPanel.Visibility = Visibility.Collapsed;
                CardActionSequenceHost.Content = null;
                LoadContent();
                BindCardList();
            });
        }

        private void OnDuplicateCard(object sender, RoutedEventArgs e)
        {
            if (!(CardList.SelectedItem is CardDefinition card)) return;
            var id = StableIds.New();
            PushOrMergeWithReload(new DuplicateCardCommand(OpenConnection, card.Id, id, card.Title + " (copy)"), () =>
            {
                LoadContent();
                var created = _vm.Content.Cards.FirstOrDefault(c => c.Id == id);
                if (created != null)
                {
                    CardList.SelectedItem = created;
                    OpenCardEditor(created);
                }
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
            card.CardTagIds = new List<string>(_cardBuffer.CardTagIds);
            card.KinkIds = new List<string>(_cardBuffer.KinkIds);
            card.RequiredEquipmentIds = new List<string>(_cardBuffer.RequiredEquipmentIds);
            card.RequiredCapabilityIds = new List<string>(_cardBuffer.RequiredCapabilityIds);
            card.Sequence = _cardBuffer.Sequence;

            // Fresh buffer over the just-saved state: dirty flag clears.
            _cardBuffer = new CardEditBuffer(card);
            SyncCardEditorFromBuffer();
            BindCardList();
            CardList.SelectedItem = card;
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
            FocusActionSequence(sequenceEditor);
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
                        args.PropertyName != nameof(ActionRowData.NumberText)) return;
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
        }

        /// <summary>Title/body keystrokes land in the buffer only.</summary>
        private void OnCardBufferFieldChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingCardEditor || _cardBuffer == null) return;
            _cardBuffer.Title = CardTitleBox.Text;
            _cardBuffer.BodyText = CardBodyBox.Text;
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
            var lines = EvaluateSelectionLines(_vm.SelectedPhase, null);
            MessageBox.Show(this, string.Join("\n", lines), "Eligible Cards — " + _vm.SelectedPhase.Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnShowSelectionDiagnostics(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedPhase == null) return;
            // Weighting context: prefer the session that's placing this phase,
            // else the selected session, else defaults.
            var weighting = _vm.SelectedSession?.CardWeighting;
            var window = new SelectionDiagnosticsWindow(_vm.Content, _vm.SelectedPhase, LoadProfileSnapshot(), weighting);
            window.Owner = this;
            window.Show();
        }

        private List<string> EvaluateSelectionLines(PhaseDefinition phase, float? happinessOverride)
        {
            var profile = CardSelectionProfile.FromProfile(LoadProfileSnapshot());
            var happiness = happinessOverride ?? 50f;
            var allTitles = phase.MustHaveAllCardTags.Select(id => TitleOf(_vm.Content.CardTagDefinitions, id)).ToList();
            var anyTitles = phase.MustHaveAnyCardTags.Select(id => TitleOf(_vm.Content.CardTagDefinitions, id)).ToList();
            var lines = new List<string>
            {
                $"Phase: {phase.Title}  ·  Happiness: {happiness:0.#}",
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
                    lines.Add($"✓ {card.Title}  (weight {weight:0.###})");
                }
                else
                {
                    lines.Add($"× {card.Title}  — {string.Join("; ", eligibility.Reasons.Select(r => r.Describe()))}");
                }
            }
            return lines;
        }

        private UserProfileSnapshot LoadProfileSnapshot()
        {
            try
            {
                var path = UserProfilePaths.ProfileDatabasePath();
                if (!System.IO.File.Exists(path)) return new UserProfileSnapshot();
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    connection.Open();
                    ProfileStore.EnsureSchema(connection);
                    return ProfileStore.Load(connection).ToSnapshot();
                }
            }
            catch (Exception)
            {
                // A broken profile must not crash authoring; diagnostics show the empty profile.
                return new UserProfileSnapshot();
            }
        }

        // ---------- Ticket 16: consumer-style session start ----------

        private void OnPlayByType(object sender, RoutedEventArgs e)
        {
            var content = _vm.Content;
            var catalog = new ContentCatalog(content);
            var profile = CardSelectionProfile.FromProfile(LoadProfileSnapshot());
            var eligibility = new SessionTypeEligibility(catalog);

            var lines = new List<string>();
            var validTypes = new List<SessionTypeDefinition>();
            foreach (var type in content.SessionTypes)
            {
                var result = eligibility.Evaluate(type, profile);
                if (result.IsEligible)
                {
                    var count = content.Sessions.Count(s => s.SessionTypeId == type.Id);
                    lines.Add($"{type.Title} — {count} session(s)");
                    if (count > 0) validTypes.Add(type);
                }
                else
                {
                    lines.Add($"{type.Title} — ineligible (missing: {string.Join(", ", result.MissingCapabilityIds)})");
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
            if (!eligibility.TrySelectSession(selectedType.Id, profile, new SystemRandomSource(), out var session))
            {
                MessageBox.Show(this, "No session of that type is available.", "Play by Type",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var player = new ReferencePlayerWindow();
            player.Owner = this;
            try
            {
                player.RunSession(session.Id);
                player.Show();
                StatusText.Text = $"Playing '{session.Title}' (type '{selectedType.Title}') — spawned with Happiness 50.";
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
            var resources = content.Resources
                .Select(r => new ActionParameterOption { Id = r.Id, Name = string.IsNullOrEmpty(r.Name) ? r.Id : r.Name })
                .ToList();
            node.ActionSequence = new ActionSequenceEditorViewModel(node, sequence?.Id,
                ActionOwnerScope.CardSequence, sequence?.Instances,
                temperatures, resources, new List<ExitOption>());
            return node.ActionSequence;
        }
    }
}
