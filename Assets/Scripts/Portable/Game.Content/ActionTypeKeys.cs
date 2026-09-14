namespace TruthCardGame.Content
{
    /// <summary>
    /// The single explicit vocabulary of Action Type keys. Everything that names
    /// an Action Type — the Core registry (Game.Core), SQLite persistence
    /// discriminators, the WPF editors, Unity host implementations — references
    /// these constants so the vocabulary cannot drift.
    ///
    /// Flow-control types, PromptChoice, and the WaitForAll barrier are always blocking;
    /// <see cref="IsAlwaysBlocking"/> is the shared rule enforced by
    /// persistence, the registry, and the executor.
    /// </summary>
    public static class ActionTypeKeys
    {
        // General (parameterized instance) types.
        public const string Debug = "debug";
        public const string StatIncrease = "statIncrease";
        public const string IncrementProgress = "increment_progress";
        public const string ModifyTemperature = "modify_temperature";
        public const string Cutscene = "cutscene";
        public const string Dialog = "dialog";
        public const string DialogFromTags = "dialog_from_tags";
        public const string Delay = "delay";
        public const string ToyActivity = "toy_activity";
        public const string ToySetPattern = "toy_set_pattern";
        public const string WaitForAll = "wait_for_all";
        public const string PromptChoice = "prompt_choice";
        public const string WaitForContinue = "wait_for_continue";
        public const string Perform = "perform";

        // Flow-control types.
        public const string PhaseGoto = "phase_goto";
        public const string SessionGoto = "session_goto";
        public const string Return = "return";
        public const string EndSession = "end_session";

        /// <summary>Actions whose semantics require completion can never be authored or run as nonblocking.</summary>
        public static bool IsAlwaysBlocking(string typeKey)
        {
            return typeKey == PhaseGoto
                || typeKey == SessionGoto
                || typeKey == Return
                || typeKey == EndSession
                || typeKey == WaitForContinue
                || typeKey == WaitForAll
                || typeKey == PromptChoice
                || typeKey == Perform;
        }

        /// <summary>Actions that must never be persisted or authored as blocking.</summary>
        public static bool IsAlwaysNonBlocking(string typeKey)
        {
            return typeKey == ToySetPattern;
        }
    }
}
