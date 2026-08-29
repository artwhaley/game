using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Stable action-type discriminator values, aliasing the single portable
    /// vocabulary in <see cref="ActionTypeKeys"/> (Game.Content). The first four
    /// keys were established by schema v1's configured-Action tables; they are
    /// reused unchanged by the v2 instance subtypes so migrated rows keep
    /// recognizable values. Flow-control types are always blocking — enforced by
    /// persistence, the Core registry, and the executor.
    /// </summary>
    public static class ActionType
    {
        // Legacy-established (reused by v2 instance subtypes):
        public const string Debug = ActionTypeKeys.Debug;
        public const string StatIncrease = ActionTypeKeys.StatIncrease;
        public const string Choice = "choice";          // v1 name; v2 table is prompt_choice below
        public const string Cutscene = ActionTypeKeys.Cutscene;

        // New in v2:
        public const string IncrementProgressV2 = ActionTypeKeys.IncrementProgress;
        public const string ModifyTemperatureV2 = ActionTypeKeys.ModifyTemperature;
        public const string PromptChoiceV2 = ActionTypeKeys.PromptChoice;
        public const string WaitForContinueV2 = ActionTypeKeys.WaitForContinue;
        public const string DialogV6 = ActionTypeKeys.Dialog;
        public const string DelayV6 = ActionTypeKeys.Delay;
        public const string ToyActivityV6 = ActionTypeKeys.ToyActivity;
        public const string WaitForAllV8 = ActionTypeKeys.WaitForAll;
        public const string PhaseGotoV2 = ActionTypeKeys.PhaseGoto;
        public const string SessionGotoV2 = ActionTypeKeys.SessionGoto;
        public const string ReturnV2 = ActionTypeKeys.Return;
        public const string EndSessionV2 = ActionTypeKeys.EndSession;

        /// <summary>Flow-control actions can never be authored as nonblocking.</summary>
        public static bool IsAlwaysBlocking(string actionType)
        {
            return ActionTypeKeys.IsAlwaysBlocking(actionType);
        }
    }
}
