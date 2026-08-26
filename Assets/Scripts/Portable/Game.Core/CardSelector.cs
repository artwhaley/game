using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Draws a random card from a deck matching tag filters. Deck entries are
    /// Card IDs resolved through the catalog.
    ///
    /// Matching semantics (preserved from baseline):
    /// - null/empty deck entries are skipped defensively (dense lists have none);
    /// - a card matches when it has ALL must-include tags and NONE of the
    ///   must-exclude tags;
    /// - comparison is default string equality (ordinal, case-sensitive);
    /// - selection is uniform over the matching list via [0, count) on the
    ///   injected card-draw random source;
    /// - draws are with replacement: nothing records or prevents repeats.
    /// </summary>
    public sealed class CardSelector
    {
        private readonly CardDeckDefinition _deck;
        private readonly ContentCatalog _catalog;

        public CardSelector(CardDeckDefinition deck, ContentCatalog catalog)
        {
            _deck = deck ?? throw new System.ArgumentNullException(nameof(deck));
            _catalog = catalog ?? throw new System.ArgumentNullException(nameof(catalog));
        }

        public bool TryDrawCard(
            IReadOnlyList<string> mustInclude,
            IReadOnlyList<string> mustExclude,
            IRandomSource cardRng,
            out CardDefinition card)
        {
            var matches = MatchingCards(mustInclude, mustExclude);
            if (matches.Count == 0)
            {
                card = null;
                return false;
            }
            card = matches[cardRng.NextInt(0, matches.Count)];
            return true;
        }

        private List<CardDefinition> MatchingCards(IReadOnlyList<string> mustInclude, IReadOnlyList<string> mustExclude)
        {
            var matches = new List<CardDefinition>();
            var ids = _deck.CardIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (string.IsNullOrEmpty(id)) continue;

                var candidate = _catalog.CardById(id);
                if (!HasAllTags(candidate.Tags, mustInclude)) continue;
                if (HasAnyTag(candidate.Tags, mustExclude)) continue;
                matches.Add(candidate);
            }
            return matches;
        }

        private static bool HasAllTags(List<string> tags, IReadOnlyList<string> required)
        {
            if (required == null || required.Count == 0) return true;
            for (var i = 0; i < required.Count; i++)
            {
                if (!tags.Contains(required[i])) return false;
            }
            return true;
        }

        private static bool HasAnyTag(List<string> tags, IReadOnlyList<string> forbidden)
        {
            if (forbidden == null || forbidden.Count == 0) return false;
            for (var i = 0; i < forbidden.Count; i++)
            {
                if (tags.Contains(forbidden[i])) return true;
            }
            return false;
        }
    }
}
