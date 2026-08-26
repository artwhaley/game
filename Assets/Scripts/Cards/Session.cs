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
        /// Shallow conversion: one PhaseSlot per referenced Phase (transitional
        /// deterministic ids), collecting each Phase once into the builder.
        /// </summary>
        public TruthCardGame.Content.SessionDefinition ToDefinition(UnityContentGraphBuilder builder)
        {
            var definition = new TruthCardGame.Content.SessionDefinition { Id = id, Title = title };
            if (tags != null) definition.Tags.AddRange(tags);
            if (phases != null)
            {
                for (var i = 0; i < phases.Count; i++)
                {
                    var phase = phases[i];
                    if (phase == null) continue;
                    builder?.CollectPhase(phase);

                    var slotId = $"legacy-slot:{id}:{phase.Id}:{i}";
                    var slot = new TruthCardGame.Content.PhaseSlotDefinition { Id = slotId, Title = phase.Title };
                    slot.Candidates.Add(new TruthCardGame.Content.PhaseSlotCandidateDefinition
                    {
                        Id = $"legacy-candidate:{slotId}:0",
                        PhaseId = phase.Id
                    });
                    definition.PhaseSlots.Add(slot);
                }
            }
            return definition;
        }
    }
}