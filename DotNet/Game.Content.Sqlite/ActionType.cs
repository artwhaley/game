namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Stable action-type discriminator values.
    ///
    /// The first four were established by schema v1's configured-Action tables;
    /// they are reused unchanged by the v2 instance subtypes so migrated rows
    /// keep recognizable values. The remainder are new in v2
    /// (action_instance.action_type). Flow-control types are always blocking —
    /// enforced by repositories and the Core Action Type registry (Ticket 05).
    /// </summary>
    public static class ActionType
    {
        // Legacy-established (reused by v2 instance subtypes):
        public const string Debug = "debug";
        public const string StatIncrease = "statIncrease";
        public const string Choice = "choice";          // v1 name; v2 table is prompt_choice below
        public const string Cutscene = "cutscene";

        // New in v2:
        public const string IncrementProgressV2 = "increment_progress";
        public const string ModifyTemperatureV2 = "modify_temperature";
        public const string PromptChoiceV2 = "prompt_choice";
        public const string PhaseGotoV2 = "phase_goto";
        public const string SessionGotoV2 = "session_goto";
        public const string ReturnV2 = "return";
        public const string EndSessionV2 = "end_session";

        /// <summary>Flow-control actions can never be authored as nonblocking.</summary>
        public static bool IsAlwaysBlocking(string actionType)
        {
            return actionType == PhaseGotoV2
                || actionType == SessionGotoV2
                || actionType == ReturnV2
                || actionType == EndSessionV2;
        }
    }
}
