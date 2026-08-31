using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>Editor-only persisted Action Block metadata.</summary>
    public sealed class ActionBlockDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int FormatVersion { get; set; }
        public string TemplateJson { get; set; }
        public int SortOrder { get; set; }
    }

    [DataContract]
    public sealed class ActionBlockTemplate
    {
        [DataMember(Order = 1)] public int FormatVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string SourcePhaseId { get; set; } = "";
        [DataMember(Order = 3)] public List<ActionBlockActionTemplate> Actions { get; set; } = new List<ActionBlockActionTemplate>();
    }

    [DataContract]
    public sealed class ActionBlockActionTemplate
    {
        [DataMember(Order = 1)] public string TypeKey { get; set; }
        [DataMember(Order = 2)] public bool IsBlocking { get; set; }
        [DataMember(Order = 3)] public string Text { get; set; }
        [DataMember(Order = 4)] public float Number { get; set; }
        [DataMember(Order = 5)] public float SecondaryNumber { get; set; }
        [DataMember(Order = 6)] public string Pattern { get; set; }
        [DataMember(Order = 7)] public List<string> References { get; set; } = new List<string>();
        [DataMember(Order = 8)] public List<ActionBlockOptionTemplate> Options { get; set; } = new List<ActionBlockOptionTemplate>();
    }

    [DataContract]
    public sealed class ActionBlockOptionTemplate
    {
        [DataMember(Order = 1)] public string Label { get; set; }
        [DataMember(Order = 2)] public List<ActionBlockActionTemplate> Actions { get; set; } = new List<ActionBlockActionTemplate>();
    }

    /// <summary>
    /// Versioned, explicit serializer for editor templates. It deliberately
    /// serializes a flat DTO vocabulary instead of CLR polymorphic type names,
    /// so templates remain readable and safe when runtime classes evolve.
    /// </summary>
    public static class ActionBlockSerializer
    {
        public const int CurrentFormatVersion = 1;

        public static string Serialize(IEnumerable<ActionInstanceDefinition> instances, string sourcePhaseId = null)
        {
            var template = new ActionBlockTemplate { SourcePhaseId = sourcePhaseId ?? "" };
            foreach (var instance in instances ?? new List<ActionInstanceDefinition>())
                template.Actions.Add(ToTemplate(instance));
            ValidateTemplate(template);
            return Write(template);
        }

        public static ActionBlockTemplate Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Action Block template is empty.");
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ActionBlockTemplate));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var template = serializer.ReadObject(stream) as ActionBlockTemplate;
                    if (template == null) throw new InvalidOperationException("Action Block template has no document.");
                    if (template.FormatVersion != CurrentFormatVersion)
                        throw new InvalidOperationException("Unsupported Action Block format version " + template.FormatVersion + "; expected " + CurrentFormatVersion + ".");
                    if (template.Actions == null) template.Actions = new List<ActionBlockActionTemplate>();
                    ValidateTemplate(template);
                    return template;
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Action Block template is corrupt: " + ex.Message, ex);
            }
        }

        public static List<ActionInstanceDefinition> Materialize(ActionBlockTemplate template, Func<int, string> idForOrdinal)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (idForOrdinal == null) throw new ArgumentNullException(nameof(idForOrdinal));
            ValidateTemplate(template);
            var result = new List<ActionInstanceDefinition>();
            for (var i = 0; i < template.Actions.Count; i++)
                result.Add(MaterializeAction(template.Actions[i], idForOrdinal(i)));
            return result;
        }

        private static string Write(ActionBlockTemplate template)
        {
            var serializer = new DataContractJsonSerializer(typeof(ActionBlockTemplate));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, template);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static ActionBlockActionTemplate ToTemplate(ActionInstanceDefinition source)
        {
            if (source == null) throw new InvalidOperationException("Cannot save a null Action in an Action Block.");
            var info = ActionTypeRegistry.ForInstance(source);
            var item = new ActionBlockActionTemplate { TypeKey = info.TypeKey, IsBlocking = source.IsBlocking };
            switch (source)
            {
                case DebugInstanceDefinition debug: item.Text = debug.Message; item.Number = debug.DelaySeconds; break;
                case StatIncreaseInstanceDefinition stat: item.Text = stat.StatKey; item.Number = stat.Amount; break;
                case IncrementProgressInstanceDefinition progress: item.Number = progress.Amount; break;
                case ModifyTemperatureInstanceDefinition temperature: item.Text = temperature.TemperatureId; item.Number = temperature.Amount; break;
                case CutsceneInstanceDefinition cutscene: item.Text = cutscene.ResourceId; break;
                case DialogInstanceDefinition dialog: item.Text = dialog.Text; break;
                case DialogFromTagsInstanceDefinition tags: item.References.AddRange(tags.RequiredDialogTagIds ?? new List<string>()); break;
                case DelayInstanceDefinition delay: item.Number = delay.DurationSeconds; break;
                case ToyActivityInstanceDefinition toy: item.Text = toy.CapabilityId; item.Pattern = toy.PatternResourceId; item.Number = toy.DurationSeconds; break;
                case ToySetPatternInstanceDefinition toySet: item.Text = toySet.CapabilityId; item.Pattern = toySet.PatternResourceId; break;
                case PromptChoiceInstanceDefinition choice:
                    item.Text = choice.Prompt;
                    foreach (var option in choice.Options ?? new List<PromptChoiceOptionDefinition>())
                    {
                        var optionTemplate = new ActionBlockOptionTemplate { Label = option.Label };
                        foreach (var nested in option.Sequence?.Instances ?? new List<ActionInstanceDefinition>())
                            optionTemplate.Actions.Add(ToTemplate(nested));
                        item.Options.Add(optionTemplate);
                    }
                    break;
                case PhaseGotoInstanceDefinition phaseGoto: item.Text = phaseGoto.PhaseExitId; break;
                case SessionGotoInstanceDefinition sessionGoto: item.Text = sessionGoto.Label; break;
                case WaitForContinueInstanceDefinition _:
                case WaitForAllInstanceDefinition _:
                case ReturnInstanceDefinition _:
                case EndSessionInstanceDefinition _:
                    break;
                default: throw new InvalidOperationException("No Action Block serializer mapping for '" + source.GetType().Name + "'.");
            }
            return item;
        }

        private static ActionInstanceDefinition MaterializeAction(ActionBlockActionTemplate item, string id)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.TypeKey)) throw new InvalidOperationException("Action Block contains an Action without a type key.");
            ActionInstanceDefinition action;
            switch (item.TypeKey)
            {
                case ActionTypeKeys.Debug: action = new DebugInstanceDefinition { Message = item.Text ?? "", DelaySeconds = item.Number }; break;
                case ActionTypeKeys.StatIncrease: action = new StatIncreaseInstanceDefinition { StatKey = item.Text ?? "", Amount = item.Number }; break;
                case ActionTypeKeys.IncrementProgress: action = new IncrementProgressInstanceDefinition { Amount = item.Number }; break;
                case ActionTypeKeys.ModifyTemperature: action = new ModifyTemperatureInstanceDefinition { TemperatureId = item.Text ?? "", Amount = item.Number }; break;
                case ActionTypeKeys.Cutscene: action = new CutsceneInstanceDefinition { ResourceId = item.Text ?? "" }; break;
                case ActionTypeKeys.Dialog: action = new DialogInstanceDefinition { Text = item.Text ?? "" }; break;
                case ActionTypeKeys.DialogFromTags:
                    var tags = new DialogFromTagsInstanceDefinition();
                    tags.RequiredDialogTagIds.AddRange(item.References ?? new List<string>());
                    action = tags;
                    break;
                case ActionTypeKeys.Delay: action = new DelayInstanceDefinition { DurationSeconds = item.Number }; break;
                case ActionTypeKeys.ToyActivity: action = new ToyActivityInstanceDefinition { CapabilityId = item.Text ?? "", PatternResourceId = item.Pattern ?? "", DurationSeconds = item.Number }; break;
                case ActionTypeKeys.ToySetPattern: action = new ToySetPatternInstanceDefinition { CapabilityId = item.Text ?? "", PatternResourceId = item.Pattern ?? "" }; break;
                case ActionTypeKeys.PromptChoice:
                    var choice = new PromptChoiceInstanceDefinition { Prompt = item.Text ?? "" };
                    var optionIndex = 0;
                    foreach (var option in item.Options ?? new List<ActionBlockOptionTemplate>())
                    {
                        var optionId = id + "-option-" + (++optionIndex);
                        var sequence = new ActionSequenceDefinition { Id = optionId + "-sequence" };
                        var nestedIndex = 0;
                        foreach (var nested in option.Actions ?? new List<ActionBlockActionTemplate>())
                            sequence.Instances.Add(MaterializeAction(nested, optionId + "-action-" + (++nestedIndex)));
                        choice.Options.Add(new PromptChoiceOptionDefinition { Id = optionId, Label = option.Label ?? "", Sequence = sequence });
                    }
                    action = choice;
                    break;
                case ActionTypeKeys.PhaseGoto: action = new PhaseGotoInstanceDefinition { PhaseExitId = item.Text ?? "" }; break;
                case ActionTypeKeys.SessionGoto: action = new SessionGotoInstanceDefinition { Label = item.Text ?? "" }; break;
                case ActionTypeKeys.WaitForContinue: action = new WaitForContinueInstanceDefinition(); break;
                case ActionTypeKeys.WaitForAll: action = new WaitForAllInstanceDefinition(); break;
                case ActionTypeKeys.Return: action = new ReturnInstanceDefinition(); break;
                case ActionTypeKeys.EndSession: action = new EndSessionInstanceDefinition(); break;
                default: throw new InvalidOperationException("Unsupported Action Block Action Type '" + item.TypeKey + "'.");
            }
            action.Id = id;
            action.IsBlocking = item.IsBlocking;
            return action;
        }

        private static void ValidateTemplate(ActionBlockTemplate template)
        {
            if (template.FormatVersion != CurrentFormatVersion)
                throw new InvalidOperationException("Unsupported Action Block format version " + template.FormatVersion + ".");
            if (template.Actions == null) throw new InvalidOperationException("Action Block has no Action list.");
            foreach (var action in template.Actions) ValidateAction(action);
        }

        private static void ValidateAction(ActionBlockActionTemplate action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.TypeKey)) throw new InvalidOperationException("Action Block contains an Action without a type key.");
            var info = ActionTypeRegistry.ByTypeKey(action.TypeKey);
            if (info.IsAlwaysBlocking && !action.IsBlocking)
                throw new InvalidOperationException(
                    "Action Block action '" + action.TypeKey + "' must be blocking.");
            if (info.IsAlwaysNonBlocking && action.IsBlocking)
                throw new InvalidOperationException(
                    "Action Block action '" + action.TypeKey + "' must be nonblocking.");
            foreach (var option in action.Options ?? new List<ActionBlockOptionTemplate>())
                foreach (var nested in option.Actions ?? new List<ActionBlockActionTemplate>()) ValidateAction(nested);
        }
    }
}
