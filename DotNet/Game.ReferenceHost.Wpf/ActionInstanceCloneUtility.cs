using System;
using TruthCardGame.Content;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Explicit instance-local clone used by the common Action editor.</summary>
    internal static class ActionInstanceCloneUtility
    {
        public static ActionInstanceDefinition Clone(ActionInstanceDefinition source, string newId)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ActionInstanceDefinition clone;
            switch (source)
            {
                case DebugInstanceDefinition debug:
                    clone = new DebugInstanceDefinition { Message = debug.Message, DelaySeconds = debug.DelaySeconds };
                    break;
                case StatIncreaseInstanceDefinition stat:
                    clone = new StatIncreaseInstanceDefinition { StatKey = stat.StatKey, Amount = stat.Amount };
                    break;
                case IncrementProgressInstanceDefinition progress:
                    clone = new IncrementProgressInstanceDefinition { Amount = progress.Amount };
                    break;
                case ModifyTemperatureInstanceDefinition temperature:
                    clone = new ModifyTemperatureInstanceDefinition { TemperatureId = temperature.TemperatureId, Amount = temperature.Amount };
                    break;
                case CutsceneInstanceDefinition cutscene:
                    clone = new CutsceneInstanceDefinition { ResourceId = cutscene.ResourceId };
                    break;
                case DialogInstanceDefinition dialog:
                    clone = new DialogInstanceDefinition { Text = dialog.Text };
                    break;
                case DelayInstanceDefinition delay:
                    clone = new DelayInstanceDefinition { DurationSeconds = delay.DurationSeconds };
                    break;
                case ToyActivityInstanceDefinition toy:
                    clone = new ToyActivityInstanceDefinition
                        { CapabilityId = toy.CapabilityId, Intensity = toy.Intensity, DurationSeconds = toy.DurationSeconds };
                    break;
                case PromptChoiceInstanceDefinition choice:
                {
                    var clonedChoice = new PromptChoiceInstanceDefinition { Prompt = choice.Prompt };
                    for (var i = 0; i < choice.Options.Count; i++)
                    {
                        var option = choice.Options[i];
                        var sequence = new ActionSequenceDefinition { Id = newId + "-option-" + (i + 1) + "-seq" };
                        for (var j = 0; j < (option.Sequence?.Instances?.Count ?? 0); j++)
                            sequence.Instances.Add(Clone(option.Sequence.Instances[j], sequence.Id + "-i-" + (j + 1)));
                        clonedChoice.Options.Add(new PromptChoiceOptionDefinition
                        {
                            Id = newId + "-option-" + (i + 1),
                            Label = option.Label,
                            Sequence = sequence,
                        });
                    }
                    clone = clonedChoice;
                    break;
                }
                case WaitForContinueInstanceDefinition:
                    clone = new WaitForContinueInstanceDefinition();
                    break;
                case PhaseGotoInstanceDefinition phaseGoto:
                    clone = new PhaseGotoInstanceDefinition { PhaseExitId = phaseGoto.PhaseExitId };
                    break;
                case SessionGotoInstanceDefinition sessionGoto:
                    clone = new SessionGotoInstanceDefinition { Label = sessionGoto.Label };
                    break;
                case ReturnInstanceDefinition:
                    clone = new ReturnInstanceDefinition();
                    break;
                case EndSessionInstanceDefinition:
                    clone = new EndSessionInstanceDefinition();
                    break;
                case WaitForAllInstanceDefinition:
                    clone = new WaitForAllInstanceDefinition();
                    break;
                default:
                    throw new InvalidOperationException("No explicit clone mapping for '" + source.GetType().Name + "'.");
            }
            clone.Id = newId;
            clone.IsBlocking = source.IsBlocking;
            return clone;
        }
    }
}
