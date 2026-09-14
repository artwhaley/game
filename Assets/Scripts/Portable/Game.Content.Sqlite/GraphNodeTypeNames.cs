namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Stable values of phase_graph_node.node_type / session_graph_node.node_type
    /// as written by DatabaseInitializer/repositories and read by the snapshot
    /// loader. Single source of truth for both sides.
    /// </summary>
    public static class GraphNodeTypeNames
    {
        // Phase-graph nodes.
        public const string PhaseEntry = "Entry";
        public const string CardExecutor = "CardExecutor";
        public const string VariableCheck = "VariableCheck";
        public const string Action = "Action";
        public const string Decision = "Decision";
        public const string Return = "Return";

        // Session-graph nodes.
        public const string SessionStart = "Start";
        public const string SessionEnd = "End";
        public const string SessionPhaseReference = "PhaseReference";
        public const string SessionDecision = "Decision";
    }
}
