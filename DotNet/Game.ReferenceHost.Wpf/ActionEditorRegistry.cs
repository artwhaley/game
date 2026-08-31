using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>One catalog item shown by the reusable Action picker.</summary>
    public sealed class ActionTypeChoice
    {
        public ActionTypeChoice(ActionTypeInfo info)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
        }

        public ActionTypeInfo Info { get; }
        public string TypeKey => Info.TypeKey;
        public string DisplayLabel => Info.DisplayLabel;
        public string Category => string.IsNullOrEmpty(Info.AuthoringCategory) ? "Other" : Info.AuthoringCategory;
        public string SearchText => (Info.DisplayLabel + " " + Info.TypeKey + " " + Info.SearchKeywords).Trim();
    }

    /// <summary>A stable parameter choice for FK-backed authoring fields.</summary>
    public sealed class ActionParameterOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
    }

    /// <summary>
    /// Explicit WPF editor metadata. This is intentionally keyed by the closed
    /// registry vocabulary rather than inferred from CLR property names.
    /// </summary>
    public sealed class ActionEditorDescriptor
    {
        public string TypeKey { get; set; }
        public bool HasTextEditor { get; set; }
        public bool HasNumberEditor { get; set; }
        public bool HasSecondaryNumberEditor { get; set; }
        public bool HasChoiceEditor { get; set; }
        public bool HasSecondaryChoiceEditor { get; set; }
        public bool HasDialogTagEditor { get; set; }
        public bool HasBlockingEditor { get; set; }
        public bool IsChoiceEditable { get; set; }
        public string TextLabel { get; set; }
        public string NumberLabel { get; set; }
        public string SecondaryNumberLabel { get; set; }
        public string ChoiceLabel { get; set; }
        public string SecondaryChoiceLabel { get; set; }
        public bool IsReadOnlyDisplay { get; set; }
    }

    public static class ActionEditorRegistry
    {
        private static readonly Dictionary<string, ActionEditorDescriptor> Descriptors = BuildDescriptors();

        public static IEnumerable<ActionTypeChoice> PickerChoices(ActionOwnerScope scope)
        {
            return ActionTypeRegistry.All
                .Where(info => (info.LegalScopes & scope) != 0 && Descriptors.ContainsKey(info.TypeKey))
                .Select(info => new ActionTypeChoice(info))
                .OrderBy(choice => choice.DisplayLabel, StringComparer.OrdinalIgnoreCase);
        }

        public static ActionEditorDescriptor For(string typeKey)
        {
            if (typeKey == null || !Descriptors.TryGetValue(typeKey, out var descriptor))
                throw new InvalidOperationException("No WPF action editor registered for '" + typeKey + "'.");
            return descriptor;
        }

        private static Dictionary<string, ActionEditorDescriptor> BuildDescriptors()
        {
            return new Dictionary<string, ActionEditorDescriptor>(StringComparer.Ordinal)
            {
                [ActionTypeKeys.Debug] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.Debug, HasTextEditor = true, HasNumberEditor = true,
                    TextLabel = "Message", NumberLabel = "Delay", HasBlockingEditor = true
                },
                [ActionTypeKeys.StatIncrease] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.StatIncrease, HasChoiceEditor = true, IsChoiceEditable = true,
                    HasNumberEditor = true, ChoiceLabel = "Stat", NumberLabel = "Amount", HasBlockingEditor = true
                },
                [ActionTypeKeys.IncrementProgress] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.IncrementProgress, HasNumberEditor = true,
                    NumberLabel = "Amount", HasBlockingEditor = true
                },
                [ActionTypeKeys.ModifyTemperature] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.ModifyTemperature, HasChoiceEditor = true, HasNumberEditor = true,
                    ChoiceLabel = "Temperature", NumberLabel = "Delta", HasBlockingEditor = true
                },
                [ActionTypeKeys.Cutscene] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.Cutscene, HasChoiceEditor = true,
                    ChoiceLabel = "Resource", HasBlockingEditor = true
                },
                [ActionTypeKeys.Dialog] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.Dialog, HasTextEditor = true, TextLabel = "Dialog", HasBlockingEditor = true
                },
                [ActionTypeKeys.DialogFromTags] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.DialogFromTags, HasDialogTagEditor = true, HasBlockingEditor = true
                },
                [ActionTypeKeys.Delay] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.Delay, HasNumberEditor = true, NumberLabel = "Duration", HasBlockingEditor = true
                },
                [ActionTypeKeys.ToyActivity] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.ToyActivity, HasChoiceEditor = true, ChoiceLabel = "Capability",
                    HasSecondaryChoiceEditor = true, SecondaryChoiceLabel = "Toy Pattern",
                    HasNumberEditor = true, NumberLabel = "Duration", HasBlockingEditor = true
                },
                [ActionTypeKeys.ToySetPattern] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.ToySetPattern, HasChoiceEditor = true, ChoiceLabel = "Capability",
                    HasSecondaryChoiceEditor = true, SecondaryChoiceLabel = "Toy Pattern"
                },
                [ActionTypeKeys.PromptChoice] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.PromptChoice, HasTextEditor = true,
                    TextLabel = "Prompt"
                },
                [ActionTypeKeys.WaitForContinue] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.WaitForContinue, IsReadOnlyDisplay = true
                },
                [ActionTypeKeys.WaitForAll] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.WaitForAll, IsReadOnlyDisplay = true
                },
                [ActionTypeKeys.PhaseGoto] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.PhaseGoto, HasChoiceEditor = true,
                    ChoiceLabel = "PhaseExit"
                },
                [ActionTypeKeys.SessionGoto] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.SessionGoto, HasTextEditor = true,
                    TextLabel = "Label"
                },
                [ActionTypeKeys.Return] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.Return, IsReadOnlyDisplay = true
                },
                [ActionTypeKeys.EndSession] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.EndSession, IsReadOnlyDisplay = true
                },
            };
        }
    }

    /// <summary>
    /// Shared owner/editor state for every authored ActionSequence. The graph
    /// VM supplies the owner context and catalogs; the window only handles the
    /// semantic commands raised by the common template.
    /// </summary>
    public sealed class ActionSequenceEditorViewModel : INotifyPropertyChanged
    {
        private string _searchText = "";
        private string _selectedActionTypeKey;
        private ActionRowData _selectionAnchor;
        private readonly ObservableCollection<ActionTypeChoice> _pickerChoices = new ObservableCollection<ActionTypeChoice>();

        public ActionSequenceEditorViewModel(GraphNodeViewModel ownerNode, string sequenceId,
            ActionOwnerScope ownerScope, IEnumerable<ActionInstanceDefinition> instances,
            IEnumerable<ActionParameterOption> temperatureOptions,
            IEnumerable<ActionParameterOption> resourceOptions,
            IEnumerable<ExitOption> exitOptions,
            ObservableCollection<ActionRowData> rows = null,
            IEnumerable<ActionParameterOption> statOptions = null,
            IEnumerable<ActionParameterOption> toyCapabilityOptions = null,
            bool isPromptChoiceDescendant = false,
            IEnumerable<ActionParameterOption> toyPatternOptions = null,
            IEnumerable<RelationChoice> dialogTagOptions = null,
            string sourcePhaseId = null)
        {
            OwnerNode = ownerNode;
            SequenceId = sequenceId ?? "";
            OwnerScope = ownerScope;
            TemperatureOptions = new List<ActionParameterOption>(temperatureOptions ?? Enumerable.Empty<ActionParameterOption>());
            StatOptions = new List<ActionParameterOption>(statOptions ?? Enumerable.Empty<ActionParameterOption>());
            ResourceOptions = new List<ActionParameterOption>(resourceOptions ?? Enumerable.Empty<ActionParameterOption>());
            ToyCapabilityOptions = new List<ActionParameterOption>(toyCapabilityOptions ?? Enumerable.Empty<ActionParameterOption>());
            ToyPatternOptions = new List<ActionParameterOption>(toyPatternOptions ?? Enumerable.Empty<ActionParameterOption>());
            DialogTagOptions = new List<RelationChoice>(dialogTagOptions ?? Enumerable.Empty<RelationChoice>());
            ExitOptions = new List<ExitOption>(exitOptions ?? Enumerable.Empty<ExitOption>());
            IsPromptChoiceDescendant = isPromptChoiceDescendant;
            SourcePhaseId = sourcePhaseId ?? "";
            Rows = rows ?? new ObservableCollection<ActionRowData>();
            foreach (var instance in instances ?? Enumerable.Empty<ActionInstanceDefinition>())
                Rows.Add(PhaseGraphViewModel.ToActionRow(this, instance, Rows.Count));
            RefreshPicker();
        }

        public GraphNodeViewModel OwnerNode { get; }
        public string SequenceId { get; }
        public ActionOwnerScope OwnerScope { get; }
        public string ScopeLabel => OwnerScope == ActionOwnerScope.SessionDecisionOptionSequence ? "Session option actions" : "Actions";
        public bool IsSessionDecisionOption => OwnerScope == ActionOwnerScope.SessionDecisionOptionSequence;
        public bool IsPromptChoiceDescendant { get; }
        /// <summary>Reusable Phase owner for PhaseGoto same-Phase Block policy.</summary>
        public string SourcePhaseId { get; }
        public string IdentityPrefix => string.IsNullOrEmpty(OptionId)
            ? OwnerNode?.Id
            : OwnerNode?.Id + "-" + OptionId;
        public string OptionId { get; set; }

        public ObservableCollection<ActionRowData> Rows { get; }
        public List<ActionParameterOption> TemperatureOptions { get; }
        public List<ActionParameterOption> StatOptions { get; }
        public List<ActionParameterOption> ResourceOptions { get; }
        public List<ActionParameterOption> ToyCapabilityOptions { get; }
        public List<ActionParameterOption> ToyPatternOptions { get; }
        public List<RelationChoice> DialogTagOptions { get; }
        public List<ExitOption> ExitOptions { get; }

        public IEnumerable<ActionRowData> SelectedRows => Rows.Where(row => row.IsSelected);

        public void ClearSelection()
        {
            foreach (var row in Rows) row.IsSelected = false;
        }

        public void SelectOnly(ActionRowData row)
        {
            ClearSelection();
            if (row != null) { row.IsSelected = true; _selectionAnchor = row; }
        }

        public void ToggleSelection(ActionRowData row)
        {
            if (row != null) { row.IsSelected = !row.IsSelected; _selectionAnchor = row; }
        }

        public void SelectRange(ActionRowData row)
        {
            if (row == null) return;
            var index = Rows.IndexOf(row);
            if (index < 0) return;
            var anchor = Rows.IndexOf(_selectionAnchor);
            if (anchor < 0) anchor = index;
            var from = Math.Min(anchor, index);
            var to = Math.Max(anchor, index);
            ClearSelection();
            for (var i = from; i <= to; i++) Rows[i].IsSelected = true;
            _selectionAnchor = row;
        }

        public void SelectRowsByIds(IEnumerable<string> ids)
        {
            var wanted = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            ClearSelection();
            foreach (var row in Rows)
                if (wanted.Contains(row.InstanceId)) row.IsSelected = true;
            _selectionAnchor = Rows.FirstOrDefault(row => row.IsSelected);
        }

        public ICollectionView ActionTypePickerView { get; private set; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value ?? "";
                RefreshPicker();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
            }
        }

        public string SelectedActionTypeKey
        {
            get => _selectedActionTypeKey;
            set
            {
                if (_selectedActionTypeKey == value) return;
                _selectedActionTypeKey = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedActionTypeKey)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ActionInstanceDefinition CreateDefaultInstance(string typeKey, string instanceId)
        {
            var info = ActionTypeRegistry.ByTypeKey(typeKey);
            var instance = info.DefaultInstance();
            instance.Id = instanceId;
            if (instance is StatIncreaseInstanceDefinition stat && StatOptions.Count > 0)
                stat.StatKey = StatOptions[0].Id;
            if (instance is ModifyTemperatureInstanceDefinition temperature && TemperatureOptions.Count > 0)
                temperature.TemperatureId = TemperatureOptions[0].Id;
            if (instance is CutsceneInstanceDefinition cutscene && ResourceOptions.Count > 0)
                cutscene.ResourceId = ResourceOptions[0].Id;
            if (instance is ToyActivityInstanceDefinition toy && ToyCapabilityOptions.Count > 0)
                toy.CapabilityId = ToyCapabilityOptions[0].Id;
            if (instance is ToyActivityInstanceDefinition timedToy && ToyPatternOptions.Count > 0)
                timedToy.PatternResourceId = ToyPatternOptions[0].Id;
            if (instance is ToySetPatternInstanceDefinition setToy)
            {
                if (ToyCapabilityOptions.Count > 0) setToy.CapabilityId = ToyCapabilityOptions[0].Id;
                if (ToyPatternOptions.Count > 0) setToy.PatternResourceId = ToyPatternOptions[0].Id;
            }
            if (instance is PromptChoiceInstanceDefinition choice)
            {
                for (var i = 0; i < 2; i++)
                {
                    var optionId = instanceId + "-option-" + (i + 1);
                    choice.Options.Add(new PromptChoiceOptionDefinition
                    {
                        Id = optionId,
                        Label = "Option " + (i + 1),
                        Sequence = new ActionSequenceDefinition { Id = optionId + "-sequence" },
                    });
                }
            }
            return instance;
        }

        /// <summary>
        /// Hot-refreshes lookup/display catalogs without replacing Rows or any
        /// authored definition. Recurses through every PromptChoice option so a
        /// dirty Card remains authoritative while catalog CRUD is immediate.
        /// </summary>
        public void RefreshCatalogs(GameContentDefinition content)
        {
            content = content ?? new GameContentDefinition();
            Replace(ResourceOptions, content.Resources
                .Where(item => string.Equals(item.Kind, ResourceKinds.Cutscene, StringComparison.OrdinalIgnoreCase))
                .Select(item => new ActionParameterOption { Id = item.Id, Name = string.IsNullOrEmpty(item.Name) ? item.Id : item.Name })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
            Replace(ToyPatternOptions, content.Resources
                .Where(item => string.Equals(item.Kind, ResourceKinds.ToyPattern, StringComparison.OrdinalIgnoreCase))
                .Select(item => new ActionParameterOption { Id = item.Id, Name = string.IsNullOrEmpty(item.Name) ? item.Id : item.Name })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
            Replace(ToyCapabilityOptions, content.SmartToyCapabilityDefinitions
                .Select(item => new ActionParameterOption { Id = item.Id, Name = string.IsNullOrEmpty(item.Title) ? item.Id : item.Title })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
            Replace(DialogTagOptions, content.DialogTags
                .Select(item => new RelationChoice { Id = item.Id, DisplayName = string.IsNullOrEmpty(item.Title) ? item.Id : item.Title })
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase));

            foreach (var row in Rows)
            {
                row.NotifyCatalogsRefreshed();
                foreach (var option in row.PromptOptions)
                    option.ActionSequence?.RefreshCatalogs(content);
            }
        }

        private static void Replace<T>(List<T> target, IEnumerable<T> values)
        {
            target.Clear();
            target.AddRange(values ?? Enumerable.Empty<T>());
        }

        private void RefreshPicker()
        {
            var query = (_searchText ?? "").Trim();
            _pickerChoices.Clear();
            foreach (var choice in ActionEditorRegistry.PickerChoices(OwnerScope))
            {
                if (query.Length == 0 || choice.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    _pickerChoices.Add(choice);
            }
            var view = new ListCollectionView(new List<ActionTypeChoice>(_pickerChoices));
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(ActionTypeChoice.DisplayLabel), ListSortDirection.Ascending));
            ActionTypePickerView = view;
            if (string.IsNullOrEmpty(SelectedActionTypeKey) && _pickerChoices.Count > 0)
                SelectedActionTypeKey = _pickerChoices[0].TypeKey;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionTypePickerView)));
        }
    }

    public static class ActionAuthoringGuards
    {
        public static string CreationError(ActionSequenceEditorViewModel sequence, string typeKey)
        {
            if (sequence == null) return "Action sequence is unavailable.";
            if ((typeKey == ActionTypeKeys.ToyActivity || typeKey == ActionTypeKeys.ToySetPattern) &&
                sequence.ToyCapabilityOptions.Count == 0)
            {
                return "Add a Smart Toy Capability before authoring a toy pattern action.";
            }
            return null;
        }
    }

    /// <summary>
    /// PlayerStats is intentionally schema-free. The authoring dropdown is
    /// therefore populated from stable stat keys already used anywhere in the
    /// content graph, while remaining editable so the first use can define a
    /// new key.
    /// </summary>
    public static class ConfiguredActionParameters
    {
        public static List<ActionParameterOption> StatOptions(GameContentDefinition content)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (content == null) return new List<ActionParameterOption>();

            foreach (var card in content.Cards ?? new List<CardDefinition>())
                CollectSequence(card?.Sequence, keys);
            foreach (var phase in content.Phases ?? new List<PhaseDefinition>())
            {
                var nodes = phase?.Graph?.Nodes;
                if (nodes == null) continue;
                foreach (var node in nodes)
                {
                    if (node is VariableCheckNodeDefinition check && check.SourceKind == VariableSourceKind.Stat)
                        Add(keys, check.VariableKey);
                    if (node is ActionNodeDefinition action) CollectSequence(action.Sequence, keys);
                    if (node is PhaseDecisionNodeDefinition decision)
                        foreach (var option in decision.Options) CollectSequence(option?.Sequence, keys);
                }
            }
            foreach (var session in content.Sessions ?? new List<SessionDefinition>())
            {
                var nodes = session?.Graph?.Nodes;
                if (nodes == null) continue;
                foreach (var node in nodes)
                {
                    if (node is SessionDecisionNodeDefinition decision)
                        foreach (var option in decision.Options) CollectSequence(option?.Sequence, keys);
                }
            }

            return keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .Select(key => new ActionParameterOption { Id = key, Name = key })
                .ToList();
        }

        private static void CollectSequence(ActionSequenceDefinition sequence, HashSet<string> keys)
        {
            if (sequence == null) return;
            foreach (var instance in sequence.Instances)
            {
                if (instance is StatIncreaseInstanceDefinition stat) Add(keys, stat.StatKey);
                if (instance is PromptChoiceInstanceDefinition choice)
                    foreach (var option in choice.Options) CollectSequence(option?.Sequence, keys);
            }
        }

        private static void Add(HashSet<string> keys, string key)
        {
            if (!string.IsNullOrWhiteSpace(key)) keys.Add(key.Trim());
        }
    }
}
