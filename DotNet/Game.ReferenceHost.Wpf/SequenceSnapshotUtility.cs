using System;
using TruthCardGame.Content;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Deep snapshot helper that preserves every existing sequence/instance/option ID.</summary>
    internal static class SequenceSnapshotUtility
    {
        public static ActionSequenceDefinition Clone(ActionSequenceEditorViewModel source)
        {
            var clone = new ActionSequenceDefinition { Id = source?.SequenceId ?? "" };
            if (source == null) return clone;
            foreach (var row in source.Rows)
            {
                clone.Instances.Add(CloneInstance(row.Definition));
            }
            return clone;
        }

        public static ActionSequenceDefinition Clone(ActionSequenceDefinition source)
        {
            var clone = new ActionSequenceDefinition { Id = source?.Id ?? "" };
            if (source == null) return clone;
            foreach (var instance in source.Instances)
            {
                clone.Instances.Add(CloneInstance(instance));
            }
            return clone;
        }

        private static ActionInstanceDefinition CloneInstance(ActionInstanceDefinition source)
        {
            var copy = ActionInstanceCloneUtility.Clone(source, source.Id);
            if (copy is PromptChoiceInstanceDefinition choice && source is PromptChoiceInstanceDefinition sourceChoice)
            {
                choice.Options.Clear();
                foreach (var option in sourceChoice.Options)
                    choice.Options.Add(new PromptChoiceOptionDefinition
                    {
                        Id = option.Id,
                        Label = option.Label,
                        Sequence = Clone(option.Sequence),
                    });
            }
            return copy;
        }
    }
}
