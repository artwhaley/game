using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// What executing an Action Instance does to the surrounding flow.
    /// <see cref="Activity"/> work runs to completion (blocking ones are
    /// awaited, nonblocking ones start on the background tracker) and never
    /// affects the graph locus. <see cref="ControlOrYield"/> work does not run
    /// as an activity at all: the executor reduces it to a request the graph VM
    /// performs — a transfer for GOTO/RETURN/EndSession, or a gameplay yield
    /// for WaitForContinue.
    ///
    /// WaitForAll and PromptChoice are blocking <see cref="Activity"/> work, not
    /// control: they execute inline as ordinary sequence steps and are tracked
    /// for WaitForAll purposes. This distinction is declared here instead of
    /// being re-derived from blocking flags and instance-type exceptions.
    /// </summary>
    public enum ActionExecutionKind
    {
        Activity,
        ControlOrYield,
    }

    /// <summary>
    /// One registry entry per Action Type — the explicit, non-reflective
    /// vocabulary of what may be authored and run. Describes the stable key,
    /// display label, legal owner scopes, blocking configurability/default,
    /// default instance values, execution kind, and the WPF editor
    /// discriminator (a stable token, not a control reference, so Core stays
    /// host-agnostic).
    ///
    /// Adding a new Action Type is the documented vertical slice in
    /// Docs/GraphWorkbench/01-architecture-decisions.md §16 — this registry is
    /// its static home. No reflection, no plugin language.
    /// </summary>
    public sealed class ActionTypeInfo
    {
        public string TypeKey { get; set; } = "";
        public string DisplayLabel { get; set; } = "";
        public string AuthoringCategory { get; set; } = "Other";
        public string SearchKeywords { get; set; } = "";
        public ActionOwnerScope LegalScopes { get; set; } = ActionOwnerScope.None;

        /// <summary>
        /// Activity (default) vs ControlOrYield. ControlOrYield types are
        /// always blocking; the registry fails loudly if one is not.
        /// </summary>
        public ActionExecutionKind ExecutionKind { get; set; } = ActionExecutionKind.Activity;

        public bool IsAlwaysBlocking { get; set; }
        public bool IsAlwaysNonBlocking { get; set; }
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
            if (info.IsAlwaysBlocking && !instance.IsBlocking)
            {
                throw new InvalidOperationException(
                    $"Action '{instance.Id}' ({info.TypeKey}) must be blocking.");
            }
            if (info.IsAlwaysNonBlocking && instance.IsBlocking)
            {
                throw new InvalidOperationException(
                    $"Action '{instance.Id}' ({info.TypeKey}) must be nonblocking.");
            }
        }

        public static string KeyOf(ActionInstanceDefinition instance)
        {
            if (instance is DebugInstanceDefinition) return ActionTypeKeys.Debug;
            if (instance is StatIncreaseInstanceDefinition) return ActionTypeKeys.StatIncrease;
            if (instance is IncrementProgressInstanceDefinition) return ActionTypeKeys.IncrementProgress;
            if (instance is ModifyTemperatureInstanceDefinition) return ActionTypeKeys.ModifyTemperature;
            if (instance is CutsceneInstanceDefinition) return ActionTypeKeys.Cutscene;
            if (instance is DialogFromTagsInstanceDefinition) return ActionTypeKeys.DialogFromTags;
            if (instance is DialogInstanceDefinition) return ActionTypeKeys.Dialog;
            if (instance is DelayInstanceDefinition) return ActionTypeKeys.Delay;
            if (instance is ToySetPatternInstanceDefinition) return ActionTypeKeys.ToySetPattern;
            if (instance is ToyActivityInstanceDefinition) return ActionTypeKeys.ToyActivity;
            if (instance is WaitForAllInstanceDefinition) return ActionTypeKeys.WaitForAll;
            if (instance is PromptChoiceInstanceDefinition) return ActionTypeKeys.PromptChoice;
            if (instance is WaitForContinueInstanceDefinition) return ActionTypeKeys.WaitForContinue;
            if (instance is PerformInstanceDefinition) return ActionTypeKeys.Perform;
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
                AuthoringCategory = "Debug/Development",
                SearchKeywords = "log trace development",
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
                AuthoringCategory = "State",
                SearchKeywords = "stat modify value",
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
                AuthoringCategory = "State",
                SearchKeywords = "progress increment phase",
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
                AuthoringCategory = "State",
                SearchKeywords = "temperature happiness delta mutation",
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
                AuthoringCategory = "Activity/Host",
                SearchKeywords = "play cutscene resource activity",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = true,
                EditorDiscriminator = "Cutscene",
                DefaultInstance = () => new CutsceneInstanceDefinition { Id = "", ResourceId = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.Dialog,
                DisplayLabel = "Dialog",
                AuthoringCategory = "Activity/Host",
                SearchKeywords = "dialog dialogue text speech line",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = true,
                EditorDiscriminator = "Dialog",
                DefaultInstance = () => new DialogInstanceDefinition { Id = "", Text = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.DialogFromTags,
                DisplayLabel = "Dialog From Tags",
                AuthoringCategory = "Activity/Host",
                SearchKeywords = "dialog tags snippet random flavor",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = true,
                EditorDiscriminator = "DialogFromTags",
                DefaultInstance = () => new DialogFromTagsInstanceDefinition { Id = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.Delay,
                DisplayLabel = "Delay",
                AuthoringCategory = "Pacing/Input",
                SearchKeywords = "delay wait timer duration",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = true,
                EditorDiscriminator = "Delay",
                DefaultInstance = () => new DelayInstanceDefinition { Id = "", DurationSeconds = 1f, IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.ToyActivity,
                DisplayLabel = "Timed Toy Pattern",
                AuthoringCategory = "Activity/Host",
                SearchKeywords = "toy smart capability pattern duration timed",
                LegalScopes = ActionOwnerScope.All,
                BlockingConfigurable = true,
                DefaultBlocking = true,
                EditorDiscriminator = "ToyActivity",
                DefaultInstance = () => new ToyActivityInstanceDefinition
                    { Id = "", CapabilityId = "", PatternResourceId = "", DurationSeconds = 10f, IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.ToySetPattern,
                DisplayLabel = "Set Toy Pattern",
                AuthoringCategory = "Activity/Host",
                SearchKeywords = "toy set pattern persistent capability",
                // Persistent toy state is never a background Task: it returns
                // promptly and survives Cards/Phases. Always nonblocking.
                LegalScopes = ActionOwnerScope.All,
                IsAlwaysNonBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = false,
                EditorDiscriminator = "ToySetPattern",
                DefaultInstance = () => new ToySetPatternInstanceDefinition
                    { Id = "", CapabilityId = "", PatternResourceId = "", IsBlocking = false },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.Perform,
                DisplayLabel = "Perform",
                AuthoringCategory = "Activity/Host",
                SearchKeywords = "perform performance event stage acting",
                // Perform is an ordinary blocking activity legal anywhere a card,
                // phase or choice sequence can run; it has no authored toggle.
                LegalScopes = ActionOwnerScope.All,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "Perform",
                DefaultInstance = () => new PerformInstanceDefinition { Id = "", EventId = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.WaitForAll,
                DisplayLabel = "Wait For All",
                AuthoringCategory = "Pacing/Input",
                SearchKeywords = "wait background complete barrier",
                LegalScopes = ActionOwnerScope.All,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "WaitForAll",
                DefaultInstance = () => new WaitForAllInstanceDefinition { Id = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.PromptChoice,
                DisplayLabel = "Prompt Choice",
                AuthoringCategory = "Pacing/Input",
                SearchKeywords = "choice input prompt",
                LegalScopes = ActionOwnerScope.All,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "PromptChoice",
                DefaultInstance = () => new PromptChoiceInstanceDefinition { Id = "", Prompt = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.PhaseGoto,
                DisplayLabel = "Phase GOTO",
                AuthoringCategory = "Flow",
                SearchKeywords = "goto phase exit transfer",
                // Cards are content leaves. Phase transfer is owned by the
                // Phase graph (or an inherited PromptChoice inside it), never
                // by a Card's own action sequence.
                LegalScopes = ActionOwnerScope.PhaseActionSequence
                            | ActionOwnerScope.ChoiceOptionSequence,
                ExecutionKind = ActionExecutionKind.ControlOrYield,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "PhaseGoto",
                DefaultInstance = () => new PhaseGotoInstanceDefinition { Id = "", PhaseExitId = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.WaitForContinue,
                DisplayLabel = "Wait For Continue",
                AuthoringCategory = "Pacing/Input",
                SearchKeywords = "wait yield continue pause",
                LegalScopes = ActionOwnerScope.All,
                ExecutionKind = ActionExecutionKind.ControlOrYield,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "WaitForContinue",
                DefaultInstance = () => new WaitForContinueInstanceDefinition { Id = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.SessionGoto,
                DisplayLabel = "Session GOTO",
                AuthoringCategory = "Flow",
                SearchKeywords = "goto session branch transfer",
                LegalScopes = ActionOwnerScope.SessionDecisionOptionSequence,
                ExecutionKind = ActionExecutionKind.ControlOrYield,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "SessionGoto",
                DefaultInstance = () => new SessionGotoInstanceDefinition { Id = "", Label = "", IsBlocking = true },
            });

            Add(new ActionTypeInfo
            {
                TypeKey = ActionTypeKeys.Return,
                DisplayLabel = "RETURN",
                AuthoringCategory = "Flow",
                SearchKeywords = "return resume continuation",
                LegalScopes = ActionOwnerScope.All,
                ExecutionKind = ActionExecutionKind.ControlOrYield,
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
                AuthoringCategory = "Flow",
                SearchKeywords = "end stop terminal",
                LegalScopes = ActionOwnerScope.All,
                ExecutionKind = ActionExecutionKind.ControlOrYield,
                IsAlwaysBlocking = true,
                BlockingConfigurable = false,
                DefaultBlocking = true,
                EditorDiscriminator = "EndSession",
                DefaultInstance = () => new EndSessionInstanceDefinition { Id = "", IsBlocking = true },
            });

            // Invariant: control/yield work is performed by the graph VM, so it
            // must be always blocking. A nonblocking ControlOrYield type would
            // be started as background work and its transfer silently dropped.
            foreach (var info in registry.Values)
            {
                if (info.ExecutionKind == ActionExecutionKind.ControlOrYield && !info.IsAlwaysBlocking)
                {
                    throw new InvalidOperationException(
                        $"ActionTypeRegistry: '{info.TypeKey}' is ControlOrYield but not always blocking.");
                }
            }

            return registry;
        }
    }
}
