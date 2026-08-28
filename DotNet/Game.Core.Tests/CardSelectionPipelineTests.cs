using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 07 HARD gate: the ordered eligibility pipeline with typed,
    /// machine-readable reasons. Ticket 08: the exact weighting formula at
    /// the contract's sanity points. Ticket 09: weighted selection with
    /// deterministic seeds and the typed no-eligible failure.
    /// </summary>
    [TestFixture]
    public class CardSelectionPipelineTests
    {
        private static CardSelectionProfile Profile(params (string Kink, KinkPreference Preference)[] kinks)
        {
            var profile = new CardSelectionProfile();
            foreach (var (kink, preference) in kinks)
            {
                profile.KinkPreferences[kink] = preference;
            }
            return profile;
        }

        private static CardDefinition Card(string id, string[] tags = null, string[] kinks = null,
            string[] equipment = null, string[] capabilities = null)
        {
            var card = new CardDefinition { Id = id, Title = id };
            if (tags != null) card.CardTagIds.AddRange(tags);
            if (kinks != null) card.KinkIds.AddRange(kinks);
            if (equipment != null) card.RequiredEquipmentIds.AddRange(equipment);
            if (capabilities != null) card.RequiredCapabilityIds.AddRange(capabilities);
            return card;
        }

        private static PhaseDefinition Phase(string[] allTags = null, string[] anyTags = null)
        {
            var phase = new PhaseDefinition { Id = "p1" };
            if (allTags != null) phase.MustHaveAllCardTags.AddRange(allTags);
            if (anyTags != null) phase.MustHaveAnyCardTags.AddRange(anyTags);
            return phase;
        }

        private static SessionCardWeightingDefinition Weights(
            float lb = 1f, float lg = 1f, float kb = 1f, float kg = 1f, float tb = 1f, float tg = 1f)
        {
            return new SessionCardWeightingDefinition
            {
                LoveBase = lb, LoveHappinessGain = lg,
                LikeBase = kb, LikeHappinessGain = kg,
                TortureBase = tb, TortureUnhappinessGain = tg,
            };
        }

        // ---------- Ticket 07: eligibility pipeline ----------

        [Test]
        public void EmptyQuery_AdmitsAllCards()
        {
            var cards = new[] { Card("c1"), Card("c2") };
            var results = CardEligibilityEngine.Evaluate(cards, Phase(), Profile());
            Assert.IsTrue(results.All(r => r.IsEligible));
        }

        [Test]
        public void AllQuery_RequiresEveryTag()
        {
            var cards = new[] { Card("c1", tags: new[] { "a" }), Card("c2", tags: new[] { "a", "b" }) };
            var results = CardEligibilityEngine.Evaluate(cards, Phase(allTags: new[] { "a", "b" }), Profile());

            Assert.IsFalse(results[0].IsEligible);
            Assert.IsTrue(results[1].IsEligible);
            Assert.AreEqual(CardRejectionReasonKind.MissingAllTag, results[0].Reasons[0].Kind);
            Assert.AreEqual("b", results[0].Reasons[0].Id, "machine-readable id names the missing tag");
        }

        [Test]
        public void AnyQuery_RequiresAtLeastOne()
        {
            var cards = new[] { Card("c1", tags: new[] { "x" }), Card("c2", tags: new[] { "y" }) };
            var results = CardEligibilityEngine.Evaluate(cards, Phase(anyTags: new[] { "y", "z" }), Profile());

            Assert.IsFalse(results[0].IsEligible);
            Assert.IsTrue(results[1].IsEligible);
            Assert.AreEqual(CardRejectionReasonKind.MissingAnyTag, results[0].Reasons[0].Kind);
        }

        [Test]
        public void AllAndAnyQueries_BothMustPass()
        {
            var cards = new[]
            {
                Card("c-fails-any", tags: new[] { "a", "other" }),
                Card("c-fails-all", tags: new[] { "y" }),
                Card("c-passes", tags: new[] { "a", "y" }),
            };
            var results = CardEligibilityEngine.Evaluate(cards, Phase(allTags: new[] { "a" }, anyTags: new[] { "y", "z" }), Profile());

            Assert.IsFalse(results[0].IsEligible, "passes ALL but fails ANY");
            Assert.IsFalse(results[1].IsEligible, "passes ANY but fails ALL");
            Assert.IsTrue(results[2].IsEligible);
        }

        [Test]
        public void DontConsent_AlwaysRejects()
        {
            var cards = new[] { Card("c1", kinks: new[] { "k1" }) };
            var results = CardEligibilityEngine.Evaluate(cards, Phase(), Profile(("k1", KinkPreference.DontConsent)));

            Assert.IsFalse(results[0].IsEligible);
            Assert.AreEqual(CardRejectionReasonKind.KinkDontConsent, results[0].Reasons[0].Kind);
        }

        [Test]
        public void UnconfiguredKink_AlwaysRejects()
        {
            var cards = new[] { Card("c1", kinks: new[] { "k-unset" }) };
            var results = CardEligibilityEngine.Evaluate(cards, Phase(), Profile());

            Assert.IsFalse(results[0].IsEligible);
            Assert.AreEqual(CardRejectionReasonKind.KinkUnconfigured, results[0].Reasons[0].Kind);
        }

        [Test]
        public void MissingEquipment_Rejects_OwnedAdmits()
        {
            var cards = new[]
            {
                Card("c-needs-eq", equipment: new[] { "eq1" }),
                Card("c-needs-eq-too", equipment: new[] { "eq1", "eq2" }),
            };
            var profile = new CardSelectionProfile();
            profile.OwnedEquipmentIds.Add("eq1");

            var results = CardEligibilityEngine.Evaluate(cards, Phase(), profile);
            Assert.IsTrue(results[0].IsEligible);
            Assert.IsFalse(results[1].IsEligible, "all required equipment must be owned");
            Assert.AreEqual(CardRejectionReasonKind.MissingEquipment, results[1].Reasons[0].Kind);
            Assert.AreEqual("eq2", results[1].Reasons[0].Id);
        }

        [Test]
        public void MissingCapability_Rejects()
        {
            var cards = new[] { Card("c1", capabilities: new[] { "cap1" }) };
            var profile = new CardSelectionProfile();
            profile.AvailableCapabilityIds.Add("cap-other");

            var results = CardEligibilityEngine.Evaluate(cards, Phase(), profile);
            Assert.IsFalse(results[0].IsEligible);
            Assert.AreEqual(CardRejectionReasonKind.MissingCapability, results[0].Reasons[0].Kind);
        }

        [Test]
        public void MixedReasons_AreAllReported()
        {
            var card = Card("c1", kinks: new[] { "k1", "k2" }, equipment: new[] { "eq1" }, capabilities: new[] { "cap1" });
            var eligibility = CardEligibilityEngine.EvaluateOne(card, Phase(), Profile());

            Assert.IsFalse(eligibility.IsEligible);
            Assert.AreEqual(4, eligibility.Reasons.Count, "unconfigured k1, unconfigured k2, missing eq1, missing cap1");
            CollectionAssert.AreEqual(
                new[]
                {
                    CardRejectionReasonKind.KinkUnconfigured,
                    CardRejectionReasonKind.KinkUnconfigured,
                    CardRejectionReasonKind.MissingEquipment,
                    CardRejectionReasonKind.MissingCapability,
                },
                eligibility.Reasons.Select(r => r.Kind));
        }

        [Test]
        public void MixedLoveAndDontConsent_RejectsOnTheDontConsentKink()
        {
            var card = Card("c1", kinks: new[] { "k-love", "k-no" });
            var eligibility = CardEligibilityEngine.EvaluateOne(card, Phase(),
                Profile(("k-love", KinkPreference.Love), ("k-no", KinkPreference.DontConsent)));

            Assert.IsFalse(eligibility.IsEligible, "one DontConsent kink rejects the whole card");
        }

        [Test]
        public void TortureAndDontConsent_RejectsOnTheDontConsentKink()
        {
            var card = Card("c1", kinks: new[] { "k-t", "k-no" });
            var eligibility = CardEligibilityEngine.EvaluateOne(card, Phase(),
                Profile(("k-t", KinkPreference.Torture), ("k-no", KinkPreference.DontConsent)));

            Assert.IsFalse(eligibility.IsEligible);
        }

        // ---------- Ticket 08: weighting formula ----------

        [Test]
        public void ZeroKinks_WeightIsExactlyOne()
        {
            var card = Card("c1");
            Assert.AreEqual(1f, CardWeightCalculator.ComputeWeight(card, Profile(), Weights(), 0f));
            Assert.AreEqual(1f, CardWeightCalculator.ComputeWeight(card, Profile(), Weights(), 100f));
        }

        [Test]
        public void DefaultTuning_ContractSanityPoints()
        {
            // Default tuning, single-kink cards:
            // Happiness 0  -> Love 1, Like 1, Torture 2
            // Happiness 50 -> all 1.5
            // Happiness 100 -> Love 2, Like 2, Torture 1
            var love = Card("c-love", kinks: new[] { "k1" });
            var like = Card("c-like", kinks: new[] { "k1" });
            var torture = Card("c-t", kinks: new[] { "k1" });
            var profile = Profile(("k1", KinkPreference.Love), ("k2", KinkPreference.Like), ("k3", KinkPreference.Torture));
            // The profile must carry the right preference per card; rebuild per case:
            var loveProfile = Profile(("k1", KinkPreference.Love));
            var likeProfile = Profile(("k1", KinkPreference.Like));
            var tortureProfile = Profile(("k1", KinkPreference.Torture));
            var weights = Weights();

            Assert.AreEqual(1f, CardWeightCalculator.ComputeWeight(love, loveProfile, weights, 0f), 0.0001f, "Love @ h=0");
            Assert.AreEqual(1f, CardWeightCalculator.ComputeWeight(like, likeProfile, weights, 0f), 0.0001f, "Like @ h=0");
            Assert.AreEqual(2f, CardWeightCalculator.ComputeWeight(torture, tortureProfile, weights, 0f), 0.0001f, "Torture @ h=0");

            Assert.AreEqual(1.5f, CardWeightCalculator.ComputeWeight(love, loveProfile, weights, 50f), 0.0001f, "Love @ h=50");
            Assert.AreEqual(1.5f, CardWeightCalculator.ComputeWeight(like, likeProfile, weights, 50f), 0.0001f, "Like @ h=50");
            Assert.AreEqual(1.5f, CardWeightCalculator.ComputeWeight(torture, tortureProfile, weights, 50f), 0.0001f, "Torture @ h=50");

            Assert.AreEqual(2f, CardWeightCalculator.ComputeWeight(love, loveProfile, weights, 100f), 0.0001f, "Love @ h=100");
            Assert.AreEqual(2f, CardWeightCalculator.ComputeWeight(like, likeProfile, weights, 100f), 0.0001f, "Like @ h=100");
            Assert.AreEqual(1f, CardWeightCalculator.ComputeWeight(torture, tortureProfile, weights, 100f), 0.0001f, "Torture @ h=100");
        }

        [Test]
        public void PositiveOnly_ArithmeticMean()
        {
            var card = Card("c1", kinks: new[] { "k1", "k2" });
            var profile = Profile(("k1", KinkPreference.Love), ("k2", KinkPreference.Like));
            var weights = Weights(lb: 1f, lg: 1f, kb: 3f, kg: 1f);

            // h = 0.5: love = 1.5, like = 3.5 -> mean = 2.5
            Assert.AreEqual(2.5f, CardWeightCalculator.ComputeWeight(card, profile, weights, 50f), 0.0001f);
        }

        [Test]
        public void AnyTorture_UsesNormalizedCoefficients()
        {
            var card = Card("c1", kinks: new[] { "k-love", "k-torture" });
            var profile = Profile(("k-love", KinkPreference.Love), ("k-torture", KinkPreference.Torture));

            // h = 0: love = 1, torture = 2
            // weighted = 2*1 + 1*0.5 = 2.5; coefficients = 1 + 0.5 = 1.5 -> 2.5/1.5
            Assert.AreEqual(2.5f / 1.5f, CardWeightCalculator.ComputeWeight(card, profile, Weights(), 0f), 0.0001f);
        }

        [Test]
        public void NonDefaultTuning_ShiftsScores()
        {
            var card = Card("c1", kinks: new[] { "k1" });
            var profile = Profile(("k1", KinkPreference.Love));
            var weights = Weights(lb: 0f, lg: 4f);

            // Love = 0 + 4*h: h=0 -> 0 (clamped to 0.01); h=100 -> 4.
            Assert.AreEqual(0.01f, CardWeightCalculator.ComputeWeight(card, profile, weights, 0f), 0.0001f, "minimum clamp");
            Assert.AreEqual(4f, CardWeightCalculator.ComputeWeight(card, profile, weights, 100f), 0.0001f);
        }

        [Test]
        public void MinimumWeight_IsNeverBelow001()
        {
            var card = Card("c1", kinks: new[] { "k1" });
            var profile = Profile(("k1", KinkPreference.Like));
            var zeroed = Weights(kb: 0f, kg: 0f);

            Assert.AreEqual(0.01f, CardWeightCalculator.ComputeWeight(card, profile, zeroed, 50f), 0.00001f);
        }

        // ---------- Ticket 09: weighted selection ----------

        private static CardSelector Selector(params CardDefinition[] cards)
        {
            var content = new GameContentDefinition();
            content.Cards.AddRange(cards);
            return new CardSelector(content, new ContentCatalog(content));
        }

        /// <summary>Ticket RNG: draws land inside the walked buckets deterministically.</summary>
        private sealed class TicketRng : IRandomSource
        {
            private readonly Queue<float> _tickets;
            public TicketRng(params float[] tickets) { _tickets = new Queue<float>(tickets); }
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat(float minInclusive, float maxExclusive)
                => _tickets.Count == 0 ? minInclusive : _tickets.Dequeue();
        }

        [Test]
        public void Draw_UsesProvidedWeights_TicketWalksBuckets()
        {
            // heavy = 4.0 (only kink, Love at h=50 with lb=1,lg=7 -> 4.5?) — use
            // deterministic simple setup: two zero-kink cards weigh 1.0 each,
            // so buckets are [0,1)=c1 and [1,2)=c2.
            var selector = Selector(Card("c1"), Card("c2"));

            Assert.AreEqual("c1", selector.Draw(Phase(), Profile(), Weights(), 50f, "s", new TicketRng(0.0f)).Id);
            Assert.AreEqual("c2", selector.Draw(Phase(), Profile(), Weights(), 50f, "s", new TicketRng(1.0f)).Id);
            Assert.AreEqual("c1", selector.Draw(Phase(), Profile(), Weights(), 50f, "s", new TicketRng(0.99f)).Id);
        }

        [Test]
        public void Draw_SkipsIneligible_Cards()
        {
            var selector = Selector(
                Card("c-rejected", kinks: new[] { "k1" }),
                Card("c-ok"));

            var profile = Profile(("k1", KinkPreference.DontConsent));
            var drawn = selector.Draw(Phase(), profile, Weights(), 50f, "s", new TicketRng(0f));

            // The only eligible card is c-ok regardless of the ticket.
            Assert.AreEqual("c-ok", drawn.Id);
        }

        [Test]
        public void Draw_NoEligible_ThrowsTypedFailure()
        {
            var selector = Selector(Card("c1", kinks: new[] { "k1" }));

            var ex = Assert.Throws<NoEligibleCardException>(() =>
                selector.Draw(Phase(), Profile(), Weights(), 50f, "s", new TicketRng(0f)));

            StringAssert.Contains("k1", ex.Message, "rejection summary names the kink");
            StringAssert.Contains("p1", ex.Message, "failure names the phase");
        }

        [Test]
        public void Evaluate_CarriesCandidatesReasonsAndWeights()
        {
            var selector = Selector(
                Card("c-eligible", kinks: new[] { "k1" }),
                Card("c-rejected", kinks: new[] { "k2" }));

            var profile = Profile(("k1", KinkPreference.Love));
            var result = selector.Evaluate(Phase(), profile, Weights(), 50f, "s1");

            Assert.AreEqual(2, result.Candidates.Count);
            Assert.AreEqual(1.5f, result.Candidates[0].Weight, 0.0001f, "Love weight at h=50 with defaults");
            Assert.AreEqual(0f, result.Candidates[1].Weight);
            Assert.AreEqual(CardRejectionReasonKind.KinkUnconfigured, result.Candidates[1].Reasons[0].Kind);
            Assert.AreEqual("s1", result.SessionId);
        }

        [Test]
        public void DeterministicSeeds_ProduceReproducibleDraws()
        {
            var content = new GameContentDefinition();
            content.Cards.Add(Card("c1", kinks: new[] { "k1" }));
            content.Cards.Add(Card("c2"));
            var selector = new CardSelector(content, new ContentCatalog(content));
            var profile = Profile(("k1", KinkPreference.Love));

            string FirstDraw(int seed)
            {
                var rng = new SystemRandomSource(seed);
                return selector.Draw(Phase(), profile, Weights(), 50f, "s", rng).Id;
            }

            Assert.AreEqual(FirstDraw(777), FirstDraw(777), "same seed, same draw");
        }
    }
}
