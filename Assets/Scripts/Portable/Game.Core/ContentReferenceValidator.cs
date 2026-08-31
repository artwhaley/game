using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Validates portable cross-entity references before any host service may be
    /// invoked. This deliberately contains no database knowledge: SQLite and
    /// directly-constructed Unity snapshots share the same boundary.
    /// </summary>
    public static class ContentReferenceValidator
    {
        /// <summary>
        /// Validates a detached sequence fragment with the same reference
        /// rules used for loaded Cards, Phases, and Sessions.
        /// </summary>
        public static void ValidateSequenceFragment(GameContentDefinition content,
            ActionSequenceDefinition sequence, string owner = "Action Sequence fragment")
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var resources = IndexResources(content.Resources);
            var capabilities = IndexIds(content.SmartToyCapabilityDefinitions, item => item?.Id);
            var dialogTags = IndexIds(content.DialogTags, item => item?.Id);
            ValidateSequence(sequence, owner ?? "Action Sequence fragment", resources, capabilities, dialogTags);
        }

        public static void Validate(GameContentDefinition content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            var resources = IndexResources(content.Resources);
            var capabilities = IndexIds(content.SmartToyCapabilityDefinitions, item => item?.Id);
            var dialogTags = IndexIds(content.DialogTags, item => item?.Id);

            foreach (var snippet in content.DialogSnippets ?? new List<DialogSnippetDefinition>())
            {
                if (snippet == null) continue;
                foreach (var tagId in snippet.DialogTagIds ?? new List<string>())
                {
                    if (!dialogTags.Contains(tagId ?? ""))
                    {
                        throw new InvalidOperationException(
                            $"Dialog Snippet '{snippet.Id}' references unknown Dialog Tag '{tagId}'.");
                    }
                }
            }

            foreach (var card in content.Cards ?? new List<CardDefinition>())
                ValidateSequence(card?.Sequence, "Card '" + (card?.Id ?? "<null>") + "'", resources, capabilities, dialogTags);

            foreach (var phase in content.Phases ?? new List<PhaseDefinition>())
            {
                if (phase?.Graph?.Nodes == null) continue;
                foreach (var node in phase.Graph.Nodes)
                {
                    if (node is ActionNodeDefinition action)
                    {
                        ValidateSequence(action.Sequence,
                            $"Phase '{phase.Id}' ActionNode '{node.Id}'", resources, capabilities, dialogTags);
                    }
                    else if (node is PhaseDecisionNodeDefinition decision)
                    {
                        foreach (var option in decision.Options ?? new List<PhaseDecisionOptionDefinition>())
                        {
                            ValidateSequence(option?.Sequence,
                                $"Phase '{phase.Id}' PhaseDecision option '{option?.Id ?? "<null>"}'",
                                resources, capabilities, dialogTags);
                        }
                    }
                }
            }

            foreach (var session in content.Sessions ?? new List<SessionDefinition>())
            {
                if (session?.Graph?.Nodes == null) continue;
                foreach (var node in session.Graph.Nodes)
                {
                    if (!(node is SessionDecisionNodeDefinition decision)) continue;
                    foreach (var option in decision.Options ?? new List<SessionDecisionOptionDefinition>())
                    {
                        ValidateSequence(option?.Sequence,
                            $"Session '{session.Id}' SessionDecision option '{option?.Id ?? "<null>"}'",
                            resources, capabilities, dialogTags);
                    }
                }
            }
        }

        private static void ValidateSequence(ActionSequenceDefinition sequence, string owner,
            Dictionary<string, ResourceDefinition> resources, HashSet<string> capabilities,
            HashSet<string> dialogTags)
        {
            if (sequence == null) return; // Structural validation owns missing sequences.
            foreach (var instance in sequence.Instances ?? new List<ActionInstanceDefinition>())
            {
                if (instance == null) continue; // Structural validation owns null instances.
                var typeKey = ActionTypeRegistry.KeyOf(instance);
                switch (instance)
                {
                    case CutsceneInstanceDefinition cutscene:
                        RequireResource(owner, instance.Id, typeKey, cutscene.ResourceId,
                            ResourceKinds.Cutscene, resources);
                        break;
                    case ToyActivityInstanceDefinition timedToy:
                        RequireCapability(owner, instance.Id, typeKey, timedToy.CapabilityId, capabilities);
                        RequireResource(owner, instance.Id, typeKey, timedToy.PatternResourceId,
                            ResourceKinds.ToyPattern, resources);
                        break;
                    case ToySetPatternInstanceDefinition setToy:
                        RequireCapability(owner, instance.Id, typeKey, setToy.CapabilityId, capabilities);
                        RequireResource(owner, instance.Id, typeKey, setToy.PatternResourceId,
                            ResourceKinds.ToyPattern, resources);
                        break;
                    case DialogFromTagsInstanceDefinition dialogFromTags:
                        if (dialogFromTags.RequiredDialogTagIds == null || dialogFromTags.RequiredDialogTagIds.Count == 0)
                        {
                            throw new InvalidOperationException(
                                $"{owner} action '{instance.Id}' ({typeKey}) requires at least one Dialog Tag.");
                        }
                        foreach (var tagId in dialogFromTags.RequiredDialogTagIds)
                        {
                            if (!dialogTags.Contains(tagId ?? ""))
                            {
                                throw new InvalidOperationException(
                                    $"{owner} action '{instance.Id}' ({typeKey}) references unknown Dialog Tag '{tagId}'.");
                            }
                        }
                        break;
                }

                if (instance is PromptChoiceInstanceDefinition prompt)
                {
                    foreach (var option in prompt.Options ?? new List<PromptChoiceOptionDefinition>())
                    {
                        ValidateSequence(option?.Sequence,
                            owner + " PromptChoice '" + instance.Id + "' option '" + (option?.Id ?? "<null>") + "'",
                            resources, capabilities, dialogTags);
                    }
                }
            }
        }

        private static void RequireResource(string owner, string actionId, string typeKey,
            string resourceId, string expectedKind, Dictionary<string, ResourceDefinition> resources)
        {
            if (!resources.TryGetValue(resourceId ?? "", out var resource))
            {
                throw new InvalidOperationException(
                    $"{owner} action '{actionId}' ({typeKey}) references missing Resource '{resourceId}'; " +
                    $"expected kind '{expectedKind}'.");
            }
            if (!string.Equals(resource.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{owner} action '{actionId}' ({typeKey}) references Resource '{resourceId}' " +
                    $"of kind '{resource.Kind}'; expected '{expectedKind}'.");
            }
        }

        private static void RequireCapability(string owner, string actionId, string typeKey,
            string capabilityId, HashSet<string> capabilities)
        {
            if (!capabilities.Contains(capabilityId ?? ""))
            {
                throw new InvalidOperationException(
                    $"{owner} action '{actionId}' ({typeKey}) references unknown Smart Toy Capability '{capabilityId}'.");
            }
        }

        private static Dictionary<string, ResourceDefinition> IndexResources(IEnumerable<ResourceDefinition> resources)
        {
            var result = new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
            foreach (var resource in resources ?? new List<ResourceDefinition>())
            {
                if (resource != null && !string.IsNullOrEmpty(resource.Id) && !result.ContainsKey(resource.Id))
                    result.Add(resource.Id, resource);
            }
            return result;
        }

        private static HashSet<string> IndexIds<T>(IEnumerable<T> items, Func<T, string> idOf)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items ?? new List<T>())
            {
                var id = idOf(item);
                if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
            return result;
        }
    }
}
