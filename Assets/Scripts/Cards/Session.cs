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
        [SerializeField] private string title = "Untitled Session";
        [Tooltip("Identity/metadata tags (e.g. relaxing, intense). Not used for drawing.")]
        [SerializeField] private List<string> tags = new List<string>();
        [Tooltip("Phases in order. The last phase is the authored ending — the driver returns to menu after it.")]
        [SerializeField] private List<Phase> phases = new List<Phase>();

        public string Title => title;
        public IReadOnlyList<string> Tags => tags;
        public IReadOnlyList<Phase> Phases => phases;

        public TruthCardGame.Content.SessionDefinition ToDefinition()
        {
            var definition = new TruthCardGame.Content.SessionDefinition { Title = title };
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