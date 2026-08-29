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
        private bool _suppressCardRelationEvents;
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
        }

        private void BindCatalogEntries()
        {
            var kind = SelectedCatalogKind;
            CatalogEntryList.DisplayMemberPath = nameof(CardTagDefinition.Title);
            switch (kind)
            {
                case "Session Types":
                    CatalogEntryList.ItemsSource = _vm.Content.SessionTypes.OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Card Tags":
                    CatalogEntryList.ItemsSource = _vm.Content.CardTagDefinitions.OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Kinks":
                    CatalogEntryList.ItemsSource = _vm.Content.KinkDefinitions.OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Equipment":
                    CatalogEntryList.ItemsSource = _vm.Content.EquipmentDefinitions.OrderBy(t => t.SortOrder).ToList();
                    break;
                case "Smart Toys":
                    CatalogEntryList.ItemsSource = _vm.Content.SmartToyCapabilityDefinitions.OrderBy(t => t.SortOrder).ToList();
                    break;
            }
        }

        private void OnCatalogEntryChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCatalogUsageText();
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
            var query = (CardFilter.Text ?? "").Trim();
            var cards = string.IsNullOrEmpty(query)
                ? _vm.Content.Cards.ToList()
                : _vm.Content.Cards.Where(c => (c.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            CardList.ItemsSource = cards;
            CardList.DisplayMemberPath = nameof(CardDefinition.Title);
        }

        private void OnCardFilterChanged(object sender, TextChangedEventArgs e) => BindCardList();

        private void OnCardListChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CardList.SelectedItem is CardDefinition card)
            {
                OpenCardEditor(card);
            }
            else
            {
                CloseCardEditor(promptForDirty: false);
            }
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

                BindRelationPicker(CardTagPicker, _vm.Content.CardTagDefinitions.Select(t => t.Title).ToList(),
                    _cardBuffer.CardTagIds.Select(id => TitleOf(_vm.Content.CardTagDefinitions, id)).ToList());
                BindRelationPicker(CardKinkPicker, _vm.Content.KinkDefinitions.Select(k => k.Title).ToList(),
                    _cardBuffer.KinkIds.Select(id => TitleOf(_vm.Content.KinkDefinitions, id)).ToList());
                BindRelationPicker(CardEquipmentPicker, _vm.Content.EquipmentDefinitions.Select(x => x.Title).ToList(),
                    _cardBuffer.RequiredEquipmentIds.Select(id => TitleOf(_vm.Content.EquipmentDefinitions, id)).ToList());
                BindRelationPicker(CardCapabilityPicker, _vm.Content.SmartToyCapabilityDefinitions.Select(x => x.Title).ToList(),
                    _cardBuffer.RequiredCapabilityIds.Select(id => TitleOf(_vm.Content.SmartToyCapabilityDefinitions, id)).ToList());

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
            foreach (var row in sequenceEditor.Rows)
            {
                row.PropertyChanged += (_, args) =>
                {
                    if (_syncingCardEditor) return;
                    if (args.PropertyName != nameof(ActionRowData.TextValue) &&
                        args.PropertyName != nameof(ActionRowData.NumberText)) return;
                    if (_cardBuffer == null) return;
                    _cardBuffer.ApplyRowValue(row.InstanceId, row.TextValue, ParseFloat(row.NumberText));
                    row.PersistedTextValue = row.TextValue;
                    row.PersistedNumberText = row.NumberText;
                    UpdateCardDirtyText();
                };
            }
            CardActionSequenceHost.Content = sequenceEditor;
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
        private void OnCardBufferRelationsChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCardRelationEvents || _syncingCardEditor || _cardBuffer == null) return;

            _cardBuffer.CardTagIds.Clear();
            _cardBuffer.CardTagIds.AddRange(ResolvePickerIds(CardTagPicker, _vm.Content.CardTagDefinitions));
            _cardBuffer.KinkIds.Clear();
            _cardBuffer.KinkIds.AddRange(ResolvePickerIds(CardKinkPicker, _vm.Content.KinkDefinitions));
            _cardBuffer.RequiredEquipmentIds.Clear();
            _cardBuffer.RequiredEquipmentIds.AddRange(ResolvePickerIds(CardEquipmentPicker, _vm.Content.EquipmentDefinitions));
            _cardBuffer.RequiredCapabilityIds.Clear();
            _cardBuffer.RequiredCapabilityIds.AddRange(ResolvePickerIds(CardCapabilityPicker, _vm.Content.SmartToyCapabilityDefinitions));
            UpdateCardDirtyText();
        }

        /// <summary>Adds a default instance of the picked action type to the card buffer and refreshes the rows.</summary>
        private void AddBufferedCardAction(ActionSequenceEditorViewModel sequence, string typeKey)
        {
            if (_cardBuffer == null) return;
            var instanceId = (sequence.IdentityPrefix ?? _cardBuffer.CardId) + "-action-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _cardBuffer.AddAction(typeKey, id => sequence.CreateDefaultInstance(typeKey, id));
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

        private static string TitleOf<T>(List<T> definitions, string id) where T : class
        {
            var property = typeof(T).GetProperty(nameof(CardTagDefinition.Id));
            foreach (var definition in definitions)
            {
                if ((string)property.GetValue(definition) == id)
                {
                    return (string)typeof(T).GetProperty(nameof(CardTagDefinition.Title)).GetValue(definition);
                }
            }
            return id;
        }

        private void BindRelationPicker(ListBox picker, List<string> allTitles, List<string> selectedTitles)
        {
            _suppressCardRelationEvents = true;
            try
            {
                picker.ItemsSource = allTitles;
                picker.SelectedItems.Clear();
                foreach (var title in selectedTitles)
                {
                    var item = allTitles.FirstOrDefault(t => t == title);
                    if (item != null && !picker.SelectedItems.Contains(item)) picker.SelectedItems.Add(item);
                }
            }
            finally
            {
                _suppressCardRelationEvents = false;
            }
        }

        private List<string> ResolvePickerIds<T>(ListBox picker, List<T> definitions) where T : class
        {
            var idProperty = typeof(T).GetProperty(nameof(CardTagDefinition.Id));
            var titleProperty = typeof(T).GetProperty(nameof(CardTagDefinition.Title));
            var result = new List<string>();
            foreach (var title in picker.SelectedItems.Cast<string>())
            {
                foreach (var definition in definitions)
                {
                    if ((string)titleProperty.GetValue(definition) == title)
                    {
                        result.Add((string)idProperty.GetValue(definition));
                        break;
                    }
                }
            }
            return result;
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
            var window = new SelectionDiagnosticsWindow(_vm.Content, _vm.SelectedPhase, LoadProfileSnapshot());
            window.Owner = this;
            window.Show();
        }

        private List<string> EvaluateSelectionLines(PhaseDefinition phase, float? happinessOverride)
        {
            var profile = CardSelectionProfile.FromProfile(LoadProfileSnapshot());
            var happiness = happinessOverride ?? 50f;
            var lines = new List<string>
            {
                $"Phase: {phase.Title}  ·  Happiness: {happiness:0.#}",
                $"Query: ALL [{string.Join(", ", phase.MustHaveAllCardTags)}]  ANY [{string.Join(", ", phase.MustHaveAnyCardTags)}]",
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
