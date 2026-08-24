using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Abstracts the coroutine host so the executor stays testable without a scene.</summary>
    /// Note: not named Start so implementations can still use Unity's Start() message.
    public interface ICoroutineRunner
    {
        void StartRoutine(IEnumerator routine);
    }

    /// <summary>
    /// Draws cards from a deck matching tag filters and runs their actions.
    /// Blocking actions are awaited in order; continuous actions are dispatched
    /// to the runner without waiting, so several may overlap.
    ///
    /// Filter semantics: a card matches when it has ALL must-include tags and
    /// NONE of the must-exclude tags.
    /// </summary>
    public sealed class CardExecutor
    {
        private readonly CardDeck _deck;
        private readonly List<string> _mustInclude = new List<string>();
        private readonly List<string> _mustExclude = new List<string>();
        private readonly ICoroutineRunner _runner;

        public CardExecutor(CardDeck deck, ICoroutineRunner runner, IEnumerable<string> mustInclude = null, IEnumerable<string> mustExclude = null)
        {
            _deck = deck;
            _runner = runner;
            if (mustInclude != null) _mustInclude.AddRange(mustInclude);
            if (mustExclude != null) _mustExclude.AddRange(mustExclude);
        }

        public CardDeck Deck => _deck;
        public IReadOnlyList<string> MustInclude => _mustInclude;
        public IReadOnlyList<string> MustExclude => _mustExclude;

        /// <summary>Draws a random matching card. Returns false when nothing matches.</summary>
        public bool TryDrawCard(out Card card)
        {
            var matches = MatchingCards();
            if (matches.Count == 0)
            {
                card = null;
                return false;
            }
            card = matches[Random.Range(0, matches.Count)];
            return true;
        }

        private List<Card> MatchingCards()
        {
            var matches = new List<Card>();
            foreach (var card in _deck.Cards)
            {
                if (card == null) continue;
                if (!HasAllTags(card, _mustInclude)) continue;
                if (HasAnyTag(card, _mustExclude)) continue;
                matches.Add(card);
            }
            return matches;
        }

        private static bool HasAllTags(Card card, List<string> required)
        {
            foreach (var tag in required)
            {
                if (!card.Tags.Contains(tag)) return false;
            }
            return true;
        }

        private static bool HasAnyTag(Card card, List<string> forbidden)
        {
            foreach (var tag in forbidden)
            {
                if (card.Tags.Contains(tag)) return true;
            }
            return false;
        }

        /// <summary>Runs a card's actions in order, honoring each action's blocking flag.</summary>
        public IEnumerator ExecuteCard(Card card, GameContext context)
        {
            foreach (var action in card.Actions)
            {
                if (action == null) continue;
                if (action.IsBlocking)
                {
                    yield return action.Execute(context);
                }
                else
                {
                    _runner.StartRoutine(action.Execute(context));
                }
            }
        }
    }
}
