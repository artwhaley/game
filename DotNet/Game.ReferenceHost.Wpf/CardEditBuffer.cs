using System;
using System.Collections.Generic;
using System.Linq;
using TruthCardGame.Content;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// The buffered edit state for ONE card (Milestone B round 2). The card
    /// editor's fields and its entire Action sequence edit against this
    /// buffer; NOTHING touches the database until Save Card pushes one
    /// composite undo command. Revert simply rebuilds from the definition.
    ///
    /// Plain class by design — testable without WPF.
    /// </summary>
    public sealed class CardEditBuffer
    {
        private readonly CardDefinition _original;
        private readonly ActionSequenceDefinition _originalSequence;

        public string CardId => _original.Id;

        public string Title { get; set; }
        public string BodyText { get; set; }
        public List<string> CardTagIds { get; } = new List<string>();
        public List<string> KinkIds { get; } = new List<string>();
        public List<string> RequiredEquipmentIds { get; } = new List<string>();
        public List<string> RequiredCapabilityIds { get; } = new List<string>();

        /// <summary>The working copy of the card's Action sequence (instances cloned, ids preserved).</summary>
        public ActionSequenceDefinition Sequence { get; private set; }

        /// <summary>The sequence as loaded — Save compares against this.</summary>
        public ActionSequenceDefinition OriginalSequence => _originalSequence;

        public CardEditBuffer(CardDefinition card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            _original = card;

            Title = card.Title ?? "";
            BodyText = card.BodyText ?? "";
            CardTagIds.AddRange(card.CardTagIds);
            KinkIds.AddRange(card.KinkIds);
            RequiredEquipmentIds.AddRange(card.RequiredEquipmentIds);
            RequiredCapabilityIds.AddRange(card.RequiredCapabilityIds);

            _originalSequence = CloneSequence(card.Sequence, card.Sequence?.Id ?? "");
            Sequence = CloneSequence(card.Sequence, card.Sequence?.Id ?? "");
        }

        // ---------- dirty tracking ----------

        public bool IsDirty =>
            !string.Equals(Title, _original.Title ?? "", StringComparison.Ordinal) ||
            !string.Equals(BodyText, _original.BodyText ?? "", StringComparison.Ordinal) ||
            !IdListsEqual(CardTagIds, _original.CardTagIds) ||
            !IdListsEqual(KinkIds, _original.KinkIds) ||
            !IdListsEqual(RequiredEquipmentIds, _original.RequiredEquipmentIds) ||
            !IdListsEqual(RequiredCapabilityIds, _original.RequiredCapabilityIds) ||
            !SequencesEqual(Sequence, _originalSequence);

        /// <summary>True when any card FIELD changed (title/body/relations) — Save needs the field commands.</summary>
        public bool FieldsChanged =>
            !string.Equals(Title, _original.Title ?? "", StringComparison.Ordinal) ||
            !string.Equals(BodyText, _original.BodyText ?? "", StringComparison.Ordinal) ||
            !IdListsEqual(CardTagIds, _original.CardTagIds) ||
            !IdListsEqual(KinkIds, _original.KinkIds) ||
            !IdListsEqual(RequiredEquipmentIds, _original.RequiredEquipmentIds) ||
            !IdListsEqual(RequiredCapabilityIds, _original.RequiredCapabilityIds);

        /// <summary>True when the Action sequence changed — Save needs ReplaceCardSequenceCommand.</summary>
        public bool SequenceChanged => !SequencesEqual(Sequence, _originalSequence);

        private static bool IdListsEqual(List<string> a, List<string> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Count == b.Count && a.All(b.Contains);
        }

        private static bool SequencesEqual(ActionSequenceDefinition a, ActionSequenceDefinition b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal)) return false;
            if (a.Instances.Count != b.Instances.Count) return false;
            for (var i = 0; i < a.Instances.Count; i++)
            {
                if (!InstancesEqual(a.Instances[i], b.Instances[i])) return false;
            }
            return true;
        }

        private static bool InstancesEqual(ActionInstanceDefinition a, ActionInstanceDefinition b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal)) return false;
            if (a.IsBlocking != b.IsBlocking) return false;
            switch (a)
            {
                case DebugInstanceDefinition debug:
                    var otherDebug = b as DebugInstanceDefinition;
                    return otherDebug != null &&
                        string.Equals(debug.Message, otherDebug.Message, StringComparison.Ordinal) &&
                        NullableEquals(debug.DelaySeconds, otherDebug.DelaySeconds);
                case StatIncreaseInstanceDefinition stat:
                    var otherStat = b as StatIncreaseInstanceDefinition;
                    return otherStat != null &&
                        string.Equals(stat.StatKey, otherStat.StatKey, StringComparison.Ordinal) &&
                        stat.Amount == otherStat.Amount;
                case IncrementProgressInstanceDefinition progress:
                    var otherProgress = b as IncrementProgressInstanceDefinition;
                    return otherProgress != null && progress.Amount == otherProgress.Amount;
                case ModifyTemperatureInstanceDefinition temperature:
                    var otherTemp = b as ModifyTemperatureInstanceDefinition;
                    return otherTemp != null &&
                        string.Equals(temperature.TemperatureId, otherTemp.TemperatureId, StringComparison.Ordinal) &&
                        temperature.Amount == otherTemp.Amount;
                case CutsceneInstanceDefinition cutscene:
                    var otherCutscene = b as CutsceneInstanceDefinition;
                    return otherCutscene != null &&
                        string.Equals(cutscene.ResourceId, otherCutscene.ResourceId, StringComparison.Ordinal);
                case DialogInstanceDefinition dialog:
                    return b is DialogInstanceDefinition otherDialog &&
                        string.Equals(dialog.Text, otherDialog.Text, StringComparison.Ordinal);
                case DelayInstanceDefinition delay:
                    return b is DelayInstanceDefinition otherDelay && delay.DurationSeconds == otherDelay.DurationSeconds;
                case ToyActivityInstanceDefinition toy:
                    return b is ToyActivityInstanceDefinition otherToy &&
                        string.Equals(toy.CapabilityId, otherToy.CapabilityId, StringComparison.Ordinal) &&
                        toy.Intensity == otherToy.Intensity && toy.DurationSeconds == otherToy.DurationSeconds;
                case PromptChoiceInstanceDefinition choice:
                    var otherChoice = b as PromptChoiceInstanceDefinition;
                    if (otherChoice == null ||
                        !string.Equals(choice.Prompt, otherChoice.Prompt, StringComparison.Ordinal) ||
                        choice.Options.Count != otherChoice.Options.Count) return false;
                    for (var i = 0; i < choice.Options.Count; i++)
                    {
                        if (!string.Equals(choice.Options[i].Label, otherChoice.Options[i].Label, StringComparison.Ordinal)) return false;
                        if (!SequencesEqual(choice.Options[i].Sequence, otherChoice.Options[i].Sequence)) return false;
                    }
                    return true;
                case WaitForContinueInstanceDefinition:
                    return b is WaitForContinueInstanceDefinition;
                case PhaseGotoInstanceDefinition phaseGoto:
                    var otherGoto = b as PhaseGotoInstanceDefinition;
                    return otherGoto != null &&
                        string.Equals(phaseGoto.PhaseExitId, otherGoto.PhaseExitId, StringComparison.Ordinal);
                case SessionGotoInstanceDefinition sessionGoto:
                    var otherSessionGoto = b as SessionGotoInstanceDefinition;
                    return otherSessionGoto != null &&
                        string.Equals(sessionGoto.Label, otherSessionGoto.Label, StringComparison.Ordinal);
                case ReturnInstanceDefinition:
                    return b is ReturnInstanceDefinition;
                case EndSessionInstanceDefinition:
                    return b is EndSessionInstanceDefinition;
                default:
                    return false;
            }
        }

        private static bool NullableEquals(float? a, float? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Value == b.Value;
        }

        // ---------- sequence row operations (buffer-local, never touch the DB) ----------

        /// <summary>Appends a default instance of the given action type to the buffered sequence.</summary>
        public string AddAction(string typeKey, Func<string, ActionInstanceDefinition> defaultInstanceFactory)
        {
            return AddAction(Sequence, typeKey, defaultInstanceFactory);
        }

        public string AddAction(ActionSequenceDefinition sequence, string typeKey, Func<string, ActionInstanceDefinition> defaultInstanceFactory)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            var instanceId = CardId + "-action-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            sequence.Instances.Add(defaultInstanceFactory(instanceId));
            return instanceId;
        }

        /// <summary>Appends a pre-built instance (duplicate path).</summary>
        public string AddConfiguredAction(ActionInstanceDefinition instance)
        {
            return AddConfiguredAction(Sequence, instance);
        }

        public string AddConfiguredAction(ActionSequenceDefinition sequence, ActionInstanceDefinition instance)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            sequence.Instances.Add(instance);
            return instance.Id;
        }

        public string InsertConfiguredAction(ActionSequenceDefinition sequence, ActionInstanceDefinition instance, int ordinal)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            sequence.Instances.Insert(Math.Max(0, Math.Min(ordinal, sequence.Instances.Count)), instance);
            return instance.Id;
        }

        public void RemoveAction(string instanceId)
        {
            RemoveAction(Sequence.Id, instanceId);
        }

        public void RemoveAction(string sequenceId, string instanceId)
        {
            FindSequence(sequenceId)?.Instances.RemoveAll(i => i.Id == instanceId);
        }

        /// <summary>Moves an instance one slot; returns false at the edges.</summary>
        public bool MoveAction(string instanceId, int delta)
        {
            return MoveAction(Sequence.Id, instanceId, delta);
        }

        public bool MoveAction(string sequenceId, string instanceId, int delta)
        {
            var sequence = FindSequence(sequenceId);
            if (sequence == null) return false;
            var index = sequence.Instances.FindIndex(instance => instance.Id == instanceId);
            if (index < 0) return false;
            var other = index + delta;
            if (other < 0 || other >= sequence.Instances.Count) return false;
            var moved = sequence.Instances[index];
            sequence.Instances.RemoveAt(index);
            sequence.Instances.Insert(other, moved);
            return true;
        }

        public bool MoveActionTo(string sequenceId, string instanceId, int ordinal)
        {
            var sequence = FindSequence(sequenceId);
            if (sequence == null) return false;
            var current = sequence.Instances.FindIndex(instance => instance.Id == instanceId);
            if (current < 0) return false;
            var item = sequence.Instances[current];
            sequence.Instances.RemoveAt(current);
            ordinal = Math.Max(0, Math.Min(ordinal, sequence.Instances.Count));
            sequence.Instances.Insert(ordinal, item);
            return true;
        }

        public int IndexOf(string instanceId)
        {
            return Sequence.Instances.FindIndex(instance => instance.Id == instanceId);
        }

        public ActionSequenceDefinition FindSequence(string sequenceId)
        {
            return FindSequenceRecursive(Sequence, sequenceId);
        }

        private static ActionSequenceDefinition FindSequenceRecursive(ActionSequenceDefinition sequence, string sequenceId)
        {
            if (sequence == null) return null;
            if (sequence.Id == sequenceId) return sequence;
            foreach (var instance in sequence.Instances)
            {
                if (instance is PromptChoiceInstanceDefinition choice)
                    foreach (var option in choice.Options)
                    {
                        var found = FindSequenceRecursive(option.Sequence, sequenceId);
                        if (found != null) return found;
                    }
            }
            return null;
        }

        /// <summary>
        /// Applies an inline row edit (message/amount/prompt text or numeric
        /// value) to the buffered instance — the same field semantics
        /// UpdateActionInstanceCommand persists.
        /// </summary>
        public void ApplyRowValue(string instanceId, string textValue, float? numberValue)
        {
            ApplyRowValue(Sequence.Id, instanceId, textValue, numberValue);
        }

        public void ApplyRowValue(string sequenceId, string instanceId, string textValue, float? numberValue)
        {
            var sequence = FindSequence(sequenceId);
            var index = sequence?.Instances.FindIndex(instance => instance.Id == instanceId) ?? -1;
            if (index < 0) return;
            var instance = sequence.Instances[index];
            switch (instance)
            {
                case DebugInstanceDefinition debug:
                    debug.Message = textValue ?? "";
                    if (numberValue.HasValue) debug.DelaySeconds = numberValue.Value;
                    break;
                case StatIncreaseInstanceDefinition stat:
                    stat.StatKey = textValue ?? "";
                    if (numberValue.HasValue) stat.Amount = numberValue.Value;
                    break;
                case IncrementProgressInstanceDefinition progress:
                    if (numberValue.HasValue) progress.Amount = numberValue.Value;
                    break;
                case ModifyTemperatureInstanceDefinition temperature:
                    temperature.TemperatureId = textValue ?? "";
                    if (numberValue.HasValue) temperature.Amount = numberValue.Value;
                    break;
                case CutsceneInstanceDefinition cutscene:
                    cutscene.ResourceId = textValue ?? "";
                    break;
                case DialogInstanceDefinition dialog:
                    dialog.Text = textValue ?? "";
                    break;
                case DelayInstanceDefinition delay:
                    if (numberValue.HasValue) delay.DurationSeconds = Math.Max(0f, numberValue.Value);
                    break;
                case ToyActivityInstanceDefinition toy:
                    toy.CapabilityId = textValue ?? "";
                    if (numberValue.HasValue) toy.Intensity = numberValue.Value;
                    break;
                case PromptChoiceInstanceDefinition choice:
                    choice.Prompt = textValue ?? "";
                    break;
                case PhaseGotoInstanceDefinition phaseGoto:
                    phaseGoto.PhaseExitId = textValue ?? "";
                    break;
                case SessionGotoInstanceDefinition sessionGoto:
                    sessionGoto.Label = textValue ?? "";
                    break;
            }
        }

        // ---------- clone ----------

        private static ActionSequenceDefinition CloneSequence(ActionSequenceDefinition source, string id)
        {
            if (source == null) return new ActionSequenceDefinition { Id = id };
            var clone = SequenceSnapshotUtility.Clone(source);
            clone.Id = id;
            return clone;
        }
    }
}
