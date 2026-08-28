using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Ticket 08: pure deterministic Kink/Happiness weight per
    /// Docs/MilestoneB/01-contract.md.
    ///
    /// h = Happiness / 100
    ///   LoveScore    = LoveBase + LoveHappinessGain * h
    ///   LikeScore    = LikeBase + LikeHappinessGain * h
    ///   TortureScore = TortureBase + TortureUnhappinessGain * (1 - h)
    ///
    /// Zero kinks: 1.0. Love/Like only: arithmetic mean of contributions.
    /// Any Torture kink: Torture contributes with coefficient 1.0, positive
    /// (Love/Like) with 0.5, normalized by the coefficient total. Final weight
    /// is clamped to at least 0.01 — hard impossibility is eligibility's job.
    /// </summary>
    public static class CardWeightCalculator
    {
        public const float MinimumWeight = 0.01f;

        public static float ComputeWeight(
            CardDefinition card,
            CardSelectionProfile profile,
            SessionCardWeightingDefinition weighting,
            float happiness)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (weighting == null) throw new ArgumentNullException(nameof(weighting));

            if (card.KinkIds.Count == 0) return 1f;

            var h = happiness / 100f;
            var loveScore = weighting.LoveBase + weighting.LoveHappinessGain * h;
            var likeScore = weighting.LikeBase + weighting.LikeHappinessGain * h;
            var tortureScore = weighting.TortureBase + weighting.TortureUnhappinessGain * (1f - h);

            var positiveTotal = 0f;
            var positiveCount = 0;
            var tortureTotal = 0f;
            var tortureCount = 0;

            for (var i = 0; i < card.KinkIds.Count; i++)
            {
                if (!profile.KinkPreferences.TryGetValue(card.KinkIds[i], out var preference))
                {
                    // Unconfigured cards are ineligible; weighting never sees them.
                    continue;
                }

                switch (preference)
                {
                    case KinkPreference.Love:
                        positiveTotal += loveScore;
                        positiveCount++;
                        break;
                    case KinkPreference.Like:
                        positiveTotal += likeScore;
                        positiveCount++;
                        break;
                    case KinkPreference.Torture:
                        tortureTotal += tortureScore;
                        tortureCount++;
                        break;
                    // DontConsent: ineligible; contributes nothing here.
                }
            }

            // No contributing kinks (all DontConsent/Unconfigured) — ineligible,
            // but return a safe weight rather than NaN.
            if (positiveCount + tortureCount == 0) return MinimumWeight;

            float weight;
            if (tortureCount == 0)
            {
                weight = positiveTotal / positiveCount;
            }
            else
            {
                var weightedTotal = tortureTotal * 1f + positiveTotal * 0.5f;
                var coefficientTotal = tortureCount * 1f + positiveCount * 0.5f;
                weight = weightedTotal / coefficientTotal;
            }

            return Math.Max(MinimumWeight, weight);
        }
    }
}
