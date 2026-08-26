using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>The pool of cards a game session draws from.</summary>
    [CreateAssetMenu(fileName = "CardDeck", menuName = "TruthCardGame/Card Deck")]
    public sealed class CardDeck : ScriptableObject
    {
        [SerializeField] private List<Card> cards = new List<Card>();

        public IReadOnlyList<Card> Cards => cards;

        /// <summary>
        /// Every distinct tag across the deck, in first-seen order.
        /// Used by the setup screen to build the tag toggles.
        /// </summary>
        public List<string> DistinctTags()
        {
            var seen = new HashSet<string>();
            var result = new List<string>();
            foreach (var card in cards)
            {
                if (card == null) continue;
                foreach (var tag in card.Tags)
                {
                    if (seen.Add(tag)) result.Add(tag);
                }
            }
            return result;
        }

        /// <summary>Converts to the portable deck definition, preserving order and null entries.</summary>
        public TruthCardGame.Content.CardDeckDefinition ToDefinition(CutsceneBindingRegistry registry)
        {
            var definition = new TruthCardGame.Content.CardDeckDefinition();
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    definition.Cards.Add(card == null ? null : card.ToDefinition(registry));
                }
            }
            return definition;
        }
    }
}
