using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>The pool of cards a game session draws from.</summary>
    [CreateAssetMenu(fileName = "CardDeck", menuName = "TruthCardGame/Card Deck")]
    public sealed class CardDeck : ScriptableObject
    {
        [Tooltip("Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.")]
        [SerializeField] private string id;
        [SerializeField] private List<Card> cards = new List<Card>();

        public string Id => id;
        public IReadOnlyList<Card> Cards => cards;

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

        /// <summary>
        /// Shallow conversion: ordered CardIds referencing collected Cards.
        /// Dense lists — null card entries are skipped, not position-preserved.
        /// </summary>
        public TruthCardGame.Content.CardDeckDefinition ToDefinition(UnityContentGraphBuilder builder)
        {
            var definition = new TruthCardGame.Content.CardDeckDefinition { Id = id, Title = name };
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    if (card == null) continue;
                    builder?.CollectCard(card);
                    definition.CardIds.Add(card.Id);
                }
            }
            return definition;
        }
    }
}
