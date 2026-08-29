using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Validates the owner scope of every loaded Action Instance, recursively.
    /// The loader uses this as a fail-loud boundary for malformed authored or
    /// development content; runtime execution applies the same rule when a
    /// nested sequence is entered.
    /// </summary>
    public static class ActionSequenceScopeValidator
    {
        public static void Validate(GameContentDefinition content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            foreach (var card in content.Cards)
            {
                ValidateSequence(card?.Sequence, ActionOwnerScope.CardSequence,
                    "Card '" + (card?.Id ?? "<null>") + "'");
            }

            foreach (var phase in content.Phases)
            {
                if (phase?.Graph == null) continue;
                foreach (var node in phase.Graph.Nodes)
                {
                    if (node is ActionNodeDefinition action)
                    {
                        ValidateSequence(action.Sequence, ActionOwnerScope.PhaseActionSequence,
                            "Phase '" + phase.Id + "' ActionNode '" + node.Id + "'");
                    }
                    else if (node is PhaseDecisionNodeDefinition decision)
                    {
                        foreach (var option in decision.Options)
                        {
                            ValidateSequence(option?.Sequence, ActionOwnerScope.PhaseActionSequence,
                                "Phase '" + phase.Id + "' PhaseDecision option '" + (option?.Id ?? "<null>") + "'");
                        }
                    }
                }
            }

            foreach (var session in content.Sessions)
            {
                if (session?.Graph == null) continue;
                foreach (var node in session.Graph.Nodes)
                {
                    if (!(node is SessionDecisionNodeDefinition decision)) continue;
                    foreach (var option in decision.Options)
                    {
                        ValidateSequence(option?.Sequence, ActionOwnerScope.SessionDecisionOptionSequence,
                            "Session '" + session.Id + "' SessionDecision option '" + (option?.Id ?? "<null>") + "'");
                    }
                }
            }
        }

        /// <summary>Validates a sequence owned by a PromptChoice descendant.</summary>
        public static void ValidatePromptChoiceDescendant(ActionSequenceDefinition sequence, ActionOwnerScope inheritedScope)
        {
            ValidateSequence(sequence, inheritedScope, "PromptChoice descendant", null, true);
        }

        private static void ValidateSequence(ActionSequenceDefinition sequence,
            ActionOwnerScope scope, string owner, HashSet<string> active = null, bool insidePromptChoice = false)
        {
            if (sequence == null)
                throw new InvalidOperationException(owner + " has no ActionSequence.");

            active = active ?? new HashSet<string>(StringComparer.Ordinal);
            if (!active.Add(sequence.Id ?? ""))
                throw new InvalidOperationException(owner + " contains a cyclic ActionSequence reference '" + sequence.Id + "'.");

            try
            {
                for (var ordinal = 0; ordinal < sequence.Instances.Count; ordinal++)
                {
                    var instance = sequence.Instances[ordinal];
                    if (instance == null)
                        throw new InvalidOperationException(owner + " sequence '" + sequence.Id + "' contains a null action at ordinal " + ordinal + ".");

                    try
                    {
                        if (insidePromptChoice && instance is WaitForAllInstanceDefinition)
                        {
                            throw new InvalidOperationException(
                                "Wait For All is not legal inside a PromptChoice descendant sequence.");
                        }
                        ActionTypeRegistry.ValidateScope(instance, scope);
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException(
                            owner + " sequence '" + sequence.Id + "' action '" + instance.Id +
                            "' at ordinal " + ordinal + " has an invalid owner scope: " + ex.Message, ex);
                    }

                    if (instance is PromptChoiceInstanceDefinition prompt)
                    {
                        var nestedScope = ActionOwnerScopes.NestedPromptChoice(scope);
                        foreach (var option in prompt.Options)
                        {
                            if (option == null)
                                throw new InvalidOperationException(owner + " PromptChoice '" + instance.Id + "' contains a null option.");
                            ValidateSequence(option.Sequence, nestedScope,
                                owner + " PromptChoice '" + instance.Id + "' option '" + option.Id + "'", active, true);
                        }
                    }
                }
            }
            finally
            {
                active.Remove(sequence.Id ?? "");
            }
        }
    }
}
