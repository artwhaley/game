using System.Linq;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core.Tests;

namespace TruthCardGame.Core
{
    public class CardSelectorTests
    {
        private static CardDefinition MakeCard(params string[] tags)
        {
            var card = new CardDefinition { Title = "T:" + string.Join(",", tags) };
            card.Tags.AddRange(tags);
            return card;
        }

        private static CardDeckDefinition MakeDeck(params CardDefinition[] cards)
        {
            var deck = new CardDeckDefinition();
            deck.Cards.AddRange(cards);
            return deck;
        }

        [Test]
        public void EmptyDeck_DrawsNothing()
        {
            var selector = new CardSelector(MakeDeck());
            Assert.IsFalse(selector.TryDrawCard(null, null, new FixedRandomSource(), out var card));
            Assert.IsNull(card);
        }

        [Test]
        public void DeckOfOnlyNullEntries_DrawsNothing()
        {
            var deck = MakeDeck(null, null);
            var selector = new CardSelector(deck);
            Assert.IsFalse(selector.TryDrawCard(null, null, new FixedRandomSource(), out var card));
            Assert.IsNull(card);
        }

        [Test]
        public void NullEntries_AreSkipped_ButRealCardsStillMatch()
        {
            var only = MakeCard("truth");
            var selector = new CardSelector(MakeDeck(null, only, null));

            Assert.IsTrue(selector.TryDrawCard(null, null, new FixedRandomSource(0), out var card));
            Assert.AreSame(only, card);
        }

        [Test]
        public void Include_RequiresAllTags()
        {
            var truth = MakeCard("truth");
            var dare = MakeCard("dare");
            var both = MakeCard("party", "dare");
            var selector = new CardSelector(MakeDeck(truth, dare, both));

            selector.TryDrawCard(new[] { "party", "dare" }, null, new FixedRandomSource(0), out var card);
            Assert.AreSame(both, card);
            CollectionAssert.IsSubsetOf(new[] { "party", "dare" }, card.Tags.ToList());
        }

        [Test]
        public void Exclude_RejectsAnyMatch()
        {
            var truth = MakeCard("truth");
            var party = MakeCard("party");
            var selector = new CardSelector(MakeDeck(truth, MakeCard("dare"), party));

            selector.TryDrawCard(null, new[] { "dare" }, new FixedRandomSource(0), out var first);

            CollectionAssert.DoesNotContain(first.Tags.ToList(), "dare");
        }

        [Test]
        public void NoFilters_MatchesAnyNonNullCard()
        {
            var a = MakeCard("x");
            var b = MakeCard("y");
            var selector = new CardSelector(MakeDeck(a, b));

            selector.TryDrawCard(null, null, new FixedRandomSource(0), out var first);
            selector.TryDrawCard(null, null, new FixedRandomSource(1), out var second);

            Assert.AreSame(a, first);
            Assert.AreSame(b, second);
        }

        [Test]
        public void Selection_IndexZero_PicksFirstMatch()
        {
            var first = MakeCard("m");
            var second = MakeCard("m");
            var selector = new CardSelector(MakeDeck(first, second));

            selector.TryDrawCard(new[] { "m" }, null, new FixedRandomSource(0), out var card);
            Assert.AreSame(first, card);
        }

        [Test]
        public void Selection_LastValidIndex_PicksLastMatch()
        {
            var first = MakeCard("m");
            var last = MakeCard("m");
            var selector = new CardSelector(MakeDeck(first, last));

            selector.TryDrawCard(new[] { "m" }, null, new FixedRandomSource(1), out var card);
            Assert.AreSame(last, card);
        }

        [Test]
        public void Draw_IsWithReplacement_SameIndexCanRepeat()
        {
            var a = MakeCard("m");
            var b = MakeCard("m");
            var selector = new CardSelector(MakeDeck(a, b));

            selector.TryDrawCard(new[] { "m" }, null, new FixedRandomSource(1), out var one);
            selector.TryDrawCard(new[] { "m" }, null, new FixedRandomSource(1), out var two);

            Assert.AreSame(b, one);
            Assert.AreSame(two, one);
        }

        [Test]
        public void Matching_IsCaseSensitive_LikeBaseline()
        {
            var lower = MakeCard("truth");
            var selector = new CardSelector(MakeDeck(lower));

            // Baseline List.Contains is ordinal/case-sensitive: "Truth" must NOT match "truth".
            Assert.IsFalse(selector.TryDrawCard(new[] { "Truth" }, null, new FixedRandomSource(), out _));
            Assert.IsTrue(selector.TryDrawCard(new[] { "truth" }, null, new FixedRandomSource(0), out _));
            // A differently-cased exclude tag therefore excludes nothing.
            Assert.IsTrue(selector.TryDrawCard(null, new[] { "TRUTH" }, new FixedRandomSource(0), out _));
        }

        [Test]
        public void NothingMatches_ReturnsNoResultState()
        {
            var selector = new CardSelector(MakeDeck(MakeCard("dare")));
            Assert.IsFalse(selector.TryDrawCard(new[] { "truth" }, null, new FixedRandomSource(), out var card));
            Assert.IsNull(card);
        }
    }
}
