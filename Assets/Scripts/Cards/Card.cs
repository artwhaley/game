using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// One drawable card: tags for filtering plus the actions it triggers.
    /// Data-only — all behavior lives in its action assets.
    /// </summary>
    [CreateAssetMenu(fileName = "Card", menuName = "TruthCardGame/Card")]
    public sealed class Card : ScriptableObject
    {
        [SerializeField] private string title = "Untitled Card";
        [Tooltip("Tags used by the executor's draw filters.")]
        [SerializeField] private List<string> tags = new List<string>();
        [Tooltip("Actions executed in order when this card is drawn.")]
        [SerializeField] private List<CardAction> actions = new List<CardAction>();

        public string Title => title;
        public IReadOnlyList<string> Tags => tags;
        public IReadOnlyList<CardAction> Actions => actions;
    }
}
