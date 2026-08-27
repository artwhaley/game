using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// One registry entry per Action Type — the explicit, non-reflective
    /// vocabulary of what may be authored and run. Describes the stable key,
    /// display label, legal owner scopes, blocking configurability/default,
    /// default instance values, and the WPF editor discriminator (a stable
    /// token, not a control reference, so Core stays host-agnostic).
    ///
    /// Adding a new Action Type is the documented vertical slice in
    /// Docs/GraphWorkbench/01-architecture-decisions.md §16 — this registry is
    /// its static home. No reflection, no plugin language.
    /// </summary>
    public sealed class ActionTypeInfo
    {
        public string TypeKey { get; set; } = "";
        public string DisplayLabel { get; set; } = "";
        public ActionOwnerScope LegalScopes { get; set; } = ActionOwnerScope.None;
        public bool IsAlwaysBlocking { get; set; }
        public bool BlockingConfigurable { get; set; } = true;
        public bool DefaultBlocking { get; set; } = true;

        /// <summary>Stable discriminator the WPF editor layer maps to an editor control (e.g. "StatIncrease").</summary>
        public string EditorDiscriminator { get; set; } = "";

        /// <summary>Fresh instance with the type's authored defaults (stable id assigned by the authoring layer).</summary>
        public Func<ActionInstanceDefinition> DefaultInstance { get; set; }
    }

    /// <summary>
    /// The explicit registry. Static and closed: every Action Type the game can
    /// author or execute is declared here once, and lookups fail loudly on
    /// unknown keys instead of guessing.
    /// </summary>
    public static class ActionTypeRegistry
    {
        private static readonly Dictionary<string, ActionTypeInfo> ByKey = Build();

        public static IReadOnlyCollection<ActionTypeInfo> All => ByKey.Values;

        public static ActionTypeInfo ByTypeKey(string typeKey)
        {
            if (typeKey == null || !ByKey.TryGetValue(typeKey, out var info))
            {
                throw new InvalidOperationException($"ActionTypeRegistry: unknown action type key '{typeKey}'.");
            }
            return info;
        }

        public static ActionTypeInfo ForInstance(ActionInstanceDefinition instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            return ByTypeKey(KeyOf(instance));
        }

        /// <summary>
        /// Authoring/runtime guard: the type must be legal in the sequence's owner
        /// scope. Throws with the full context instead of silently tolerating a
        /// misplaced GOTO kind.
        /// </summary>
        public static void ValidateScope(ActionInstanceDefinition instance, ActionOwnerScope scope)
        {
            var info = ForInstance(instance);
            if ((info.LegalScopes & scope) == 0)
            {
                throw new InvalidOperationException(
                    $"Action '{instance.Id}' ({info.TypeKey}) is not legal in owner scope '{scope}' " +
                    $"(legal: {info.LegalScopes}).");
            }
        }

        public static string KeyOf(ActionInstanceDefinition instance)
        {
            if (instance is DebugInstanceDefinition) return ActionTypeKeys.Debug;
            if (instance is StatIncreaseInstanceDefinition) return ActionTypeKeys.StatIncrease;
            if (instance is IncrementProgressInstanceDefinition) return ActionTypeKeys.IncrementProgress;
            if (instance is ModifyTemperatureInstanceDefinition) return ActionTypeKeys.ModifyTemperature;
            if (instance is CutsceneInstanceDefinition) return ActionTypeKeys.Cutscene;
            if (instance is PromptChoiceInstanceDefinition) return ActionTypeKeys.PromptChoice;
            if (instance is PhaseGotoInstanceDefinition) return ActionTypeKeys.PhaseGoto;
            if (instance is SessionGotoInstanceDefinition) return ActionTypeKeys.SessionGoto;
            if (instance is ReturnInstanceDefinition) return ActionTypeKeys.Return;
            if (instance is EndSessionInstanceDefinition) return ActionTypeKeys.EndSession;

            throw new InvalidOperationException(
                $"ActionTypeRegistry: no key for instance type '{instance.GetType().Name}'.");
        }

        private static Dictionary<string, ActionTypeInfo> Build()
        {
            var registry = new Dictionary<string, ActionTypeInfo>();

            void Add(ActionTypeInfo info)
            {
                registry.Add(info.TypeKey, info);
            }

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.Debug,
                DisplayLabel = "Debug Log",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = false,
                EditorDiscriminator = "Debug",
                DefaultInstance = () => new DebugInstanceDefinition { Id = "", Message = "", DelaySeconds = 0f, IsBlocking = false },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.StatIncrease,
                DisplayLabel = "Stat Increase",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = false,
                EditorDiscriminator = "StatIncrease",
                DefaultInstance = () => new StatIncreaseInstanceDefinition { Id = "", StatKey = "", Amount = 0f, IsBlocking = false },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.IncrementProgress,
                DisplayLabel = "Increment Phase Progress",
                // Progress lives on the active PhaseRun; session-level sequences
                // have no PhaseRun, so this type is illegal there.
                LegalScopes = ActionOwnerScope.CardSequence
                            | ActionOwnerScope.PhaseActionSequence
                            | ActionOwnerScope.ChoiceOptionSequence,
                BlockingConfigurable = true,
                DefaultBlocking = false,
                EditorDiscriminator = "IncrementProgress",
                DefaultInstance = () => new IncrementProgressInstanceDefinition { Id = "", Amount = 10f, IsBlocking = false },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.ModifyTemperature,
                DisplayLabel = "Modify Temperature",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = false,
                EditorDiscriminator = "ModifyTemperature",
                DefaultInstance = () => new ModifyTemperatureInstanceDefinition { Id = "", TemperatureId = "", Amount = 0f, IsBlocking = false },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.Cutscene,
                DisplayLabel = "Cutscene",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = true,
                EditorDiscriminator = "Cutscene",
                DefaultInstance = () => new CutsceneInstanceDefinition { Id = "", ResourceId = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.PromptChoice,
                DisplayLabel = "Prompt Choice",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "PromptChoice",
                DefaultInstance = () => new PromptChoiceInstanceDefinition { Id = "", Prompt = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.PhaseGoto,
                DisplayLabel = "Phase GOTO",
                LegalScopes = ActionOwnerScope.PhaseActionSequence | ActionOwnerScope.ChoiceOptionSequence,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "PhaseGoto",
                DefaultInstance = () => new PhaseGotoInstanceDefinition { Id = "", PhaseExitId = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.SessionGoto,
                DisplayLabel = "Session GOTO",
                LegalScopes = ActionOwnerScope.SessionDecisionOptionSequence,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "SessionGoto",
                DefaultInstance = () => new SessionGotoInstanceDefinition { Id = "", Label = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.Return,
                DisplayLabel = "Return",
                LegalScopes = ActionOwnerScope.All,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "Return",
                DefaultInstance = () => new ReturnInstanceDefinition { Id = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.EndSession,
                DisplayLabel = "End Session",
                LegalScopes = ActionOwnerScope.All,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "EndSession",
                DefaultInstance = () => new EndSessionInstanceDefinition { Id = "", IsBlocking = true },
            });

            return registry;
        }
    }
}
