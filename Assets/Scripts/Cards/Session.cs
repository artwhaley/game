using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// A "game type": the top-level container a player picks at the setup
    /// screen. Tags here are identity/metadata (e.g. "relaxing", "intense")
    /// used to organize and pick sessions — they never flow into the draw
    /// filter. Draw filtering lives entirely on phases.
    /// </summary>
    [CreateAssetMenu(fileName = "Session", menuName = "TruthCardGame/Session")]
    public sealed class Session : ScriptableObject
    {
        [Tooltip("Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.")]
        [SerializeField] private string id;
        [SerializeField] private string title = "Untitled Session";
        [Tooltip("Identity/metadata tags (e.g. relaxing, intense). Not used for drawing.")]
        [SerializeField] private List<string> tags = new List<string>();
        [Tooltip("Phases in order. The last phase is the authored ending — the driver returns to menu after it.")]
        [SerializeField] private List<Phase> phases = new List<Phase>();

        public string Id => id;
        public string Title => title;
        public IReadOnlyList<string> Tags => tags;
        public IReadOnlyList<Phase> Phases => phases;

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
        /// Conversion to the v2 session shape: Start → one PhaseReference per
        /// ordered phase → End, where every reference's Complete socket flows to
        /// the next reference (the last completes into SessionEnd). Each phase is
        /// collected once into the builder.
        /// </summary>
        public TruthCardGame.Content.SessionDefinition ToDefinition(UnityContentGraphBuilder builder)
        {
            var definition = new TruthCardGame.Content.SessionDefinition
            {
                Id = id,
                Title = title,
                SessionTypeId = "type-standard",
            };
            if (tags != null) definition.Tags.AddRange(tags);

            var start = new TruthCardGame.Content.SessionStartNodeDefinition { Id = "n-" + id + "-start" };
            start.Outputs.Add(new TruthCardGame.Content.GraphOutputDefinition { Id = start.Id + "-out" });
            var end = new TruthCardGame.Content.SessionEndNodeDefinition { Id = "n-" + id + "-end" };
            definition.Graph.Nodes.Add(start);
            definition.Graph.Nodes.Add(end);

            if (phases != null)
            {
                var references = new List<TruthCardGame.Content.PhaseReferenceNodeDefinition>();
                foreach (var phase in phases)
                {
                    if (phase == null) continue;
                    builder?.CollectPhase(phase);

                    var reference = new TruthCardGame.Content.PhaseReferenceNodeDefinition
                    {
                        Id = "n-" + id + "-ref-" + phase.Id,
                        PhaseId = phase.Id,
                    };
                    foreach (var exit in phase.ToDefinition().Exits)
                    {
                        reference.Outputs.Add(new TruthCardGame.Content.GraphOutputDefinition
                        {
                            Id = reference.Id + "-socket-" + exit.Name.ToLowerInvariant(),
                            Kind = TruthCardGame.Content.GraphPortKind.PhaseExit,
                            PhaseExitId = exit.Id,
                        });
                    }
                    references.Add(reference);
                    definition.Graph.Nodes.Add(reference);
                }

                // Start -> first reference.
                if (references.Count > 0)
                {
                    definition.Graph.Edges.Add(new TruthCardGame.Content.GraphEdgeDefinition
                    {
                        Id = "se-" + id + "-start",
                        SourceOutputId = start.Outputs[0].Id,
                        TargetNodeId = references[0].Id,
                    });
                }
                else
                {
                    definition.Graph.Edges.Add(new TruthCardGame.Content.GraphEdgeDefinition
                    {
                        Id = "se-" + id + "-start",
                        SourceOutputId = start.Outputs[0].Id,
                        TargetNodeId = end.Id,
                    });
                }

                // Each reference's sockets: the Complete socket flows to the next
                // reference (last one to End); any other exit also lands at End.
                for (var i = 0; i < references.Count; i++)
                {
                    foreach (var output in references[i].Outputs)
                    {
                        TruthCardGame.Content.SessionGraphNodeDefinition target =
                            (i == references.Count - 1) ? end : references[i + 1];
                        definition.Graph.Edges.Add(new TruthCardGame.Content.GraphEdgeDefinition
                        {
                            Id = $"se-{id}-{i}-{output.Id}",
                            SourceOutputId = output.Id,
                            TargetNodeId = target.Id,
                        });
                    }
                }
            }

            return definition;
        }
    }
}
