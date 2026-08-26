using System;
using System.Collections.Generic;
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
            var card = new CardDefinition
            {
                Id = "card-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Title = "T:" + string.Join(",", tags)
            };
            card.Tags.AddRange(tags);
            return card;
        }

        private static CardSelector MakeSelector(params CardDefinition[] cards)
        {
            var deck = new CardDeckDefinition { Id = "deck" };
            foreach (var card in cards) deck.CardIds.Add(card.Id);
            var catalog = new ContentCatalog(new GameContentDefinition { Deck = deck, Cards = new List<CardDefinition>(cards) });
            return new CardSelector(deck, catalog);
        }

        [Test]
        public void EmptyDeck_DrawsNothing()
        {
            var selector = MakeSelector();
            Assert.IsFalse(selector.TryDrawCard(null, null, new FixedRandomSource(), out var card));
            Assert.IsNull(card);
        }

        [Test]
        public void DeckOfOnlyEmptyEntries_DrawsNothing()
        {
            var deck = new CardDeckDefinition { Id = "deck" };
            deck.CardIds.Add(null);
            deck.CardIds.Add("");
            var catalog = new ContentCatalog(new GameContentDefinition { Deck = deck });
            var selector = new CardSelector(deck, catalog);

            Assert.IsFalse(selector.TryDrawCard(null, null, new FixedRandomSource(), out var card));
            Assert.IsNull(card);
        }

        [Test]
        public void EmptyEntries_AreSkipped_ButRealCardsStillMatch()
        {
            var only = MakeCard("truth");
            var deck = new CardDeckDefinition { Id = "deck" };
            deck.CardIds.Add(null);
            deck.CardIds.Add(only.Id);
            deck.CardIds.Add("");
            var catalog = new ContentCatalog(new GameContentDefinition { Deck = deck, Cards = { only } });
            var selector = new CardSelector(deck, catalog);

            Assert.IsTrue(selector.TryDrawCard(null, null, new FixedRandomSource(0), out var card));
            Assert.AreSame(only, card);
        }

        [Test]
        public void Include_RequiresAllTags()
        {
            var truth = MakeCard("truth");
            var dare = MakeCard("dare");
            var both = MakeCard("party", "dare");
            var selector = MakeSelector(truth, dare, both);

            selector.TryDrawCard(new[] { "party", "dare" }, null, new FixedRandomSource(0), out var card);
            Assert.AreSame(both, card);
            CollectionAssert.IsSubsetOf(new[] { "party", "dare" }, card.Tags.ToList());
        }

        [Test]
        public void Exclude_RejectsAnyMatch()
        {
            var truth = MakeCard("truth");
            var party = MakeCard("party");
            var selector = MakeSelector(truth, MakeCard("dare"), party);

            selector.TryDrawCard(null, new[] { "dare" }, new FixedRandomSource(0), out var first);

            CollectionAssert.DoesNotContain(first.Tags.ToList(), "dare");
        }

        [Test]
        public void NoFilters_MatchesAnyCard()
        {
            var a = MakeCard("x");
            var b = MakeCard("y");
            var selector = MakeSelector(a, b);

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
            var selector = MakeSelector(first, second);

            selector.TryDrawCard(new[] { "m" }, null, new FixedRandomSource(0), out var card);
            Assert.AreSame(first, card);
        }

        [Test]
        public void Selection_LastValidIndex_PicksLastMatch()
        {
            var first = MakeCard("m");
            var last = MakeCard("m");
            var selector = MakeSelector(first, last);

            selector.TryDrawCard(new[] { "m" }, null, new FixedRandomSource(1), out var card);
            Assert.AreSame(last, card);
        }

        [Test]
        public void Draw_IsWithReplacement_SameIndexCanRepeat()
        {
            var a = MakeCard("m");
            var b = MakeCard("m");
            var selector = MakeSelector(a, b);

            selector.TryDrawCard(new[] { "m" }, null, new FixedRandomSource(1), out var one);
            selector.TryDrawCard(new[] { "m" }, null, new FixedRandomSource(1), out var two);

            Assert.AreSame(b, one);
            Assert.AreSame(two, one);
        }

        [Test]
        public void Matching_IsCaseSensitive_LikeBaseline()
        {
            var lower = MakeCard("truth");
            var selector = MakeSelector(lower);

            // Baseline List.Contains is ordinal/case-sensitive: "Truth" must NOT match "truth".
            Assert.IsFalse(selector.TryDrawCard(new[] { "Truth" }, null, new FixedRandomSource(), out _));
            Assert.IsTrue(selector.TryDrawCard(new[] { "truth" }, null, new FixedRandomSource(0), out _));
            // A differently-cased exclude tag therefore excludes nothing.
            Assert.IsTrue(selector.TryDrawCard(null, new[] { "TRUTH" }, new FixedRandomSource(0), out _));
        }

        [Test]
        public void NothingMatches_ReturnsNoResultState()
        {
            var selector = MakeSelector(MakeCard("dare"));
            Assert.IsFalse(selector.TryDrawCard(new[] { "truth" }, null, new FixedRandomSource(), out var card));
            Assert.IsNull(card);
        }

        [Test]
        public void MissingCardId_FailsLoudly()
        {
            var deck = new CardDeckDefinition { Id = "deck" };
            deck.CardIds.Add("no-such-card");
            var catalog = new ContentCatalog(new GameContentDefinition { Deck = deck });
            var selector = new CardSelector(deck, catalog);

            Assert.Throws<InvalidOperationException>(
                () => selector.TryDrawCard(null, null, new FixedRandomSource(0), out _));
        }
    }
}
