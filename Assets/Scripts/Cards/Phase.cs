using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// One stage of a Session. Defines which cards get drawn (tag filter) and
    /// how long the stage lasts. Pure data — the graph VM walks phases; a
    /// phase never executes anything itself.
    ///
    /// The transitional converter builds the standard v2 executable graph
    /// (Entry → CardExecutor → progress-complete check → GOTO Complete).
    /// MinCards/MaxCards are obsolete in Core (deleted from the portable
    /// model): the serialized fields survive here only to derive a progress
    /// completion target once at conversion time, preserving the authored
    /// stage length. No min/max semantics exist in the runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "Phase", menuName = "TruthCardGame/Phase")]
    public sealed class Phase : ScriptableObject
    {
        [Tooltip("Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.")]
        [SerializeField] private string id;
        [SerializeField] private string title = "Untitled Phase";
        [Tooltip("Cards drawn during this phase must have ALL of these tags.")]
        [SerializeField] private List<string> mustIncludeTags = new List<string>();
        [Tooltip("Cards drawn during this phase must have NONE of these tags.")]
        [SerializeField] private List<string> mustExcludeTags = new List<string>();
        [Tooltip("Unscaled draw count range: the converter derives the progress target from the midpoint. Transitional data; Core has no min/max semantics.")]
        [SerializeField] private int minCards = 1;
        [SerializeField] private int maxCards = 3;

        public string Id => id;
        public string Title => title;
        public IReadOnlyList<string> MustIncludeTags => mustIncludeTags;
        public IReadOnlyList<string> MustExcludeTags => mustExcludeTags;
        public int MinCards => minCards;
        public int MaxCards => maxCards;

        /// <summary>Mints the stable ID on first call; no-op once set. Called by OnValidate and authoring tooling.</summary>
        public void EnsureId()
        {
            if (string.IsNullOrEmpty(id)) id = System.Guid.NewGuid().ToString("N");
        }

        private void OnValidate()
        {
            EnsureId();
        }

        /// <summary>
        /// Builds the v2 executable graph with one exported exit (Complete).
        /// Card cadence is graph control now: the CardExecutor draws cards that
        /// each add progress, and the progress-complete check fires the exit.
        /// </summary>
        public TruthCardGame.Content.PhaseDefinition ToDefinition()
        {
            var definition = new TruthCardGame.Content.PhaseDefinition
            {
                Id = id,
                Title = title,
            };
            if (mustIncludeTags != null) definition.MustIncludeTags.AddRange(mustIncludeTags);
            if (mustExcludeTags != null) definition.MustExcludeTags.AddRange(mustExcludeTags);

            var exitId = "px-" + id + "-complete";
            definition.Exits.Add(new TruthCardGame.Content.PhaseExitDefinition { Id = exitId, Name = "Complete" });

            // Standard template: Entry -> CardExecutor -> done(progress >= target):
            //   true -> GOTO Complete; false -> back to the executor (user-paced loop).
            var entry = Node(new TruthCardGame.Content.PhaseEntryNodeDefinition { Id = "pn-" + id + "-entry" });
            var draw = Node(new TruthCardGame.Content.CardExecutorNodeDefinition { Id = "pn-" + id + "-draw" });
            var done = new TruthCardGame.Content.VariableCheckNodeDefinition
            {
                Id = "pn-" + id + "-done",
                SourceKind = TruthCardGame.Content.VariableSourceKind.PhaseProgress,
                Operator = TruthCardGame.Content.VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = ProgressTarget(),
            };
            done.Outputs.Add(Out(done.Id + "-true", TruthCardGame.Content.GraphPortKind.True));
            done.Outputs.Add(Out(done.Id + "-false", TruthCardGame.Content.GraphPortKind.False));
            var gotoDone = ActionNode("pan-" + id + "-goto-done",
                new TruthCardGame.Content.PhaseGotoInstanceDefinition { Id = "inst-" + id + "-goto-done", PhaseExitId = exitId });

            definition.Graph.Nodes.Add(entry);
            definition.Graph.Nodes.Add(draw);
            definition.Graph.Nodes.Add(done);
            definition.Graph.Nodes.Add(gotoDone);
            definition.Graph.Edges.Add(Edge(entry.Outputs[0].Id, draw.Id));
            definition.Graph.Edges.Add(Edge(draw.Outputs[0].Id, done.Id));
            definition.Graph.Edges.Add(Edge(FindOutput(done, TruthCardGame.Content.GraphPortKind.False).Id, draw.Id));
            definition.Graph.Edges.Add(Edge(FindOutput(done, TruthCardGame.Content.GraphPortKind.True).Id, gotoDone.Id));

            return definition;
        }

        /// <summary>Midpoint of the authored draw range × 10 progress per card, floored at one card.</summary>
        private float ProgressTarget()
        {
            var midpoint = (Mathf.Max(1, minCards) + Mathf.Max(1, maxCards)) / 2f;
            return Mathf.Max(10f, midpoint * 10f);
        }

        private static T Node<T>(T node) where T : TruthCardGame.Content.GraphNodeDefinition
        {
            node.Outputs.Add(Out(node.Id + "-out"));
            return node;
        }

        private static TruthCardGame.Content.ActionNodeDefinition ActionNode(string id, params TruthCardGame.Content.ActionInstanceDefinition[] instances)
        {
            var node = new TruthCardGame.Content.ActionNodeDefinition
            {
                Id = id,
                Sequence = new TruthCardGame.Content.ActionSequenceDefinition { Id = "seq-" + id },
            };
            foreach (var instance in instances) node.Sequence.Instances.Add(instance);
            node.Outputs.Add(Out(id + "-out"));
            return node;
        }

        private static TruthCardGame.Content.GraphOutputDefinition Out(string id, TruthCardGame.Content.GraphPortKind kind = TruthCardGame.Content.GraphPortKind.Normal)
        {
            return new TruthCardGame.Content.GraphOutputDefinition { Id = id, Kind = kind };
        }

        private static TruthCardGame.Content.GraphEdgeDefinition Edge(string sourceOutputId, string targetNodeId)
        {
            return new TruthCardGame.Content.GraphEdgeDefinition
            {
                Id = "edge-" + sourceOutputId + "->" + targetNodeId,
                SourceOutputId = sourceOutputId,
                TargetNodeId = targetNodeId,
            };
        }

        private static TruthCardGame.Content.GraphOutputDefinition FindOutput(TruthCardGame.Content.GraphNodeDefinition node, TruthCardGame.Content.GraphPortKind kind)
        {
            foreach (var output in node.Outputs)
            {
                if (output.Kind == kind) return output;
            }
            throw new KeyNotFoundException($"no '{kind}' output on node '{node.Id}'.");
        }
    }
}
