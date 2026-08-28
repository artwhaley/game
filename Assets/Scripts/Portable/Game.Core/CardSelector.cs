using System;
using System.Collections.Generic;
using System.Text;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Ticket 09: diagnostic-friendly weighted Card selection. Combines the
    /// eligibility pipeline (Ticket 07) and weighting (Ticket 08) into one
    /// ordered evaluation over a Card catalog:
    ///
    ///   1. Phase Card tag query
    ///   2. Consent / Kink configuration
    ///   3. Equipment
    ///   4. Smart Toy capabilities
    ///   5. Weighting
    ///   6. Weighted RNG (PhaseRun Card RNG)
    ///
    /// Candidates are evaluated in catalog order (the order of the Cards
    /// list passed in) and drawn by cumulative-weight walk in that same
    /// order — deterministic and documented. No repeat suppression.
    /// </summary>
    public sealed class CardSelector
    {
        private readonly IReadOnlyList<CardDefinition> _cards;
        private readonly ContentCatalog _catalog;

        public CardSelector(GameContentDefinition content, ContentCatalog catalog)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            _cards = content.Cards ?? throw new ArgumentNullException(nameof(content.Cards));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>One evaluated candidate with its final weight (eligible cards only carry weights).</summary>
        public sealed class Candidate
        {
            public CardDefinition Card { get; }
            public float Weight { get; }
            public List<CardRejectionReason> Reasons { get; }

            public Candidate(CardDefinition card, float weight, List<CardRejectionReason> reasons)
            {
                Card = card;
                Weight = weight;
                Reasons = reasons;
            }
        }

        /// <summary>Full diagnostic evaluation; does not draw.</summary>
        public sealed class SelectionResult
        {
            public List<Candidate> Candidates { get; } = new List<Candidate>();
            public CardDefinition Selected { get; set; }
            public PhaseDefinition Phase { get; set; }
            public string SessionId { get; set; }
            public float Happiness { get; set; }
        }

        /// <summary>Evaluates every Card against the Phase; no RNG use.</summary>
        public SelectionResult Evaluate(PhaseDefinition phase, CardSelectionProfile profile, SessionCardWeightingDefinition weighting, float happiness, string sessionId)
        {
            if (phase == null) throw new ArgumentNullException(nameof(phase));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var result = new SelectionResult
            {
                Phase = phase,
                SessionId = sessionId,
                Happiness = happiness,
            };

            for (var i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                var eligibility = CardEligibilityEngine.EvaluateOne(card, phase, profile);
                if (eligibility.IsEligible)
                {
                    var weight = CardWeightCalculator.ComputeWeight(card, profile, weighting, happiness);
                    result.Candidates.Add(new Candidate(card, weight, eligibility.Reasons));
                }
                else
                {
                    result.Candidates.Add(new Candidate(card, 0f, eligibility.Reasons));
                }
            }

            return result;
        }

        /// <summary>
        /// Draws a Card with the PhaseRun Card RNG. Throws
        /// <see cref="NoEligibleCardException"/> when nothing passes — never
        /// skips, relaxes, or silently alters flow.
        /// </summary>
        public CardDefinition Draw(PhaseDefinition phase, CardSelectionProfile profile, SessionCardWeightingDefinition weighting, float happiness, string sessionId, IRandomSource cardRng)
        {
            var evaluation = Evaluate(phase, profile, weighting, happiness, sessionId);

            var eligible = new List<Candidate>();
            var totalWeight = 0f;
            for (var i = 0; i < evaluation.Candidates.Count; i++)
            {
                if (evaluation.Candidates[i].Weight > 0f)
                {
                    eligible.Add(evaluation.Candidates[i]);
                    totalWeight += evaluation.Candidates[i].Weight;
                }
            }

            if (eligible.Count == 0 || totalWeight <= 0f)
            {
                throw new NoEligibleCardException(BuildFailureMessage(phase, sessionId, evaluation));
            }

            var ticket = cardRng.NextFloat(0f, totalWeight);
            var walked = 0f;
            for (var i = 0; i < eligible.Count; i++)
            {
                walked += eligible[i].Weight;
                if (ticket < walked)
                {
                    evaluation.Selected = eligible[i].Card;
                    return eligible[i].Card;
                }
            }

            // Floating-point guard: the walk should never exceed the total, but
            // if it lands exactly on the boundary, take the last eligible card.
            evaluation.Selected = eligible[eligible.Count - 1].Card;
            return evaluation.Selected;
        }

        private static string BuildFailureMessage(PhaseDefinition phase, string sessionId, SelectionResult evaluation)
        {
            var message = new StringBuilder();
            message.Append("No eligible Card");
            if (!string.IsNullOrEmpty(sessionId)) message.Append(" for session '").Append(sessionId).Append("'");
            message.Append(" in phase '").Append(phase.Id).Append("'");
            if (!string.IsNullOrEmpty(phase.Title)) message.Append(" (").Append(phase.Title).Append(")");
            message.Append(".");
            message.Append(" Query: ALL [").Append(JoinIds(phase.MustHaveAllCardTags))
                .Append("], ANY [").Append(JoinIds(phase.MustHaveAnyCardTags)).Append("].");

            var rejected = 0;
            for (var i = 0; i < evaluation.Candidates.Count; i++)
            {
                if (evaluation.Candidates[i].Weight > 0f) continue;
                rejected++;
                message.Append(" Card '").Append(evaluation.Candidates[i].Card.Id)
                    .Append("': ");
                for (var r = 0; r < evaluation.Candidates[i].Reasons.Count; r++)
                {
                    if (r > 0) message.Append("; ");
                    message.Append(evaluation.Candidates[i].Reasons[r].Describe());
                }
                message.Append(".");
            }
            if (rejected == 0)
            {
                message.Append(" The Card catalog is empty.");
            }
            return message.ToString();
        }

        private static string JoinIds(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "";
            var text = new StringBuilder();
            for (var i = 0; i < ids.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(ids[i]);
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// Typed no-eligible failure (Ticket 09): carries Session, Phase, and the
    /// full rejection summary so the host can present actionable diagnostics.
    /// </summary>
    public sealed class NoEligibleCardException : Exception
    {
        public string SessionId { get; }
        public string PhaseId { get; }

        public NoEligibleCardException(string message, string sessionId = null, string phaseId = null)
            : base(message)
        {
            SessionId = sessionId;
            PhaseId = phaseId;
        }
    }
}
