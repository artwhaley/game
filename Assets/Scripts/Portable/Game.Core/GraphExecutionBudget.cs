using System;

namespace TruthCardGame.Core
{
    /// <summary>Per-RunUntilYield safety budget shared by Session/Phase/action stepping.</summary>
    public sealed class GraphExecutionBudget
    {
        public GraphExecutionBudget(int limit = 10000)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "Execution budget must be positive.");
            Limit = limit;
        }

        public int Limit { get; }
        public int Used { get; private set; }

        public void Consume(string kind, string sessionId = null, string phaseId = null, string nodeId = null)
        {
            Used++;
            if (Used <= Limit) return;

            throw new GraphExecutionException(
                $"Graph execution safety budget of {Limit} was exceeded while processing {kind}. " +
                $"Session '{sessionId ?? "?"}', Phase '{phaseId ?? "?"}', node '{nodeId ?? "?"}', " +
                $"used {Used}. The graph may contain a non-yielding loop.");
        }
    }

    /// <summary>Clear runtime failure for a graph that exceeds its safety budget.</summary>
    public sealed class GraphExecutionException : InvalidOperationException
    {
        public GraphExecutionException(string message) : base(message)
        {
        }
    }
}
