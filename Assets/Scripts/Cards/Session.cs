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

        public TruthCardGame.Content.SessionDefinition ToDefinition()
        {
            var definition = new TruthCardGame.Content.SessionDefinition { Id = id, Title = title };
            if (tags != null) definition.Tags.AddRange(tags);
            if (phases != null)
            {
                foreach (var phase in phases)
                {
                    definition.Phases.Add(phase == null ? null : phase.ToDefinition());
                }
            }
            return definition;
        }
    }
}