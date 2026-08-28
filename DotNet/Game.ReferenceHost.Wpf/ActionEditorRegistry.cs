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
        public bool HasChoiceEditor { get; set; }
        public string TextLabel { get; set; }
        public string NumberLabel { get; set; }
        public string ChoiceLabel { get; set; }
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
                .OrderBy(choice => choice.Category, StringComparer.Ordinal)
                .ThenBy(choice => choice.DisplayLabel, StringComparer.Ordinal);
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
                    TextLabel = "Message", NumberLabel = "Delay"
                },
                [ActionTypeKeys.StatIncrease] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.StatIncrease, HasTextEditor = true, HasNumberEditor = true,
                    TextLabel = "Stat", NumberLabel = "Amount"
                },
                [ActionTypeKeys.IncrementProgress] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.IncrementProgress, HasNumberEditor = true,
                    NumberLabel = "Amount"
                },
                [ActionTypeKeys.ModifyTemperature] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.ModifyTemperature, HasChoiceEditor = true, HasNumberEditor = true,
                    ChoiceLabel = "Temperature", NumberLabel = "Delta"
                },
                [ActionTypeKeys.Cutscene] = new ActionEditorDescriptor
                {
                    TypeKey = ActionTypeKeys.Cutscene, HasChoiceEditor = true,
                    ChoiceLabel = "Resource"
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
        private readonly ObservableCollection<ActionTypeChoice> _pickerChoices = new ObservableCollection<ActionTypeChoice>();

        public ActionSequenceEditorViewModel(GraphNodeViewModel ownerNode, string sequenceId,
            ActionOwnerScope ownerScope, IEnumerable<ActionInstanceDefinition> instances,
            IEnumerable<ActionParameterOption> temperatureOptions,
            IEnumerable<ActionParameterOption> resourceOptions,
            IEnumerable<ExitOption> exitOptions,
            ObservableCollection<ActionRowData> rows = null)
        {
            OwnerNode = ownerNode;
            SequenceId = sequenceId ?? "";
            OwnerScope = ownerScope;
            TemperatureOptions = new List<ActionParameterOption>(temperatureOptions ?? Enumerable.Empty<ActionParameterOption>());
            ResourceOptions = new List<ActionParameterOption>(resourceOptions ?? Enumerable.Empty<ActionParameterOption>());
            ExitOptions = new List<ExitOption>(exitOptions ?? Enumerable.Empty<ExitOption>());
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
        public string IdentityPrefix => IsSessionDecisionOption
            ? OwnerNode?.Id + "-" + (OptionId ?? "option")
            : OwnerNode?.Id;
        public string OptionId { get; set; }

        public ObservableCollection<ActionRowData> Rows { get; }
        public List<ActionParameterOption> TemperatureOptions { get; }
        public List<ActionParameterOption> ResourceOptions { get; }
        public List<ExitOption> ExitOptions { get; }

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
            if (instance is ModifyTemperatureInstanceDefinition temperature && TemperatureOptions.Count > 0)
                temperature.TemperatureId = TemperatureOptions[0].Id;
            if (instance is CutsceneInstanceDefinition cutscene && ResourceOptions.Count > 0)
                cutscene.ResourceId = ResourceOptions[0].Id;
            return instance;
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
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionTypeChoice.Category)));
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(ActionTypeChoice.Category), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription(nameof(ActionTypeChoice.DisplayLabel), ListSortDirection.Ascending));
            ActionTypePickerView = view;
            if (string.IsNullOrEmpty(SelectedActionTypeKey) && _pickerChoices.Count > 0)
                SelectedActionTypeKey = _pickerChoices[0].TypeKey;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionTypePickerView)));
        }
    }
}
