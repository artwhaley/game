using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Draws a random card from a deck matching tag filters.
    ///
    /// Matching semantics (preserved from baseline):
    /// - null deck entries are ignored;
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

        public CardSelector(CardDeckDefinition deck)
        {
            _deck = deck ?? throw new System.ArgumentNullException(nameof(deck));
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
            var cards = _deck.Cards;
            for (var i = 0; i < cards.Count; i++)
            {
                var candidate = cards[i];
                if (candidate == null) continue;
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
