using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Typed, machine-readable eligibility reasons (Ticket 07). The WPF
    /// diagnostics map over these; they are not just strings.
    /// </summary>
    public enum CardRejectionReasonKind
    {
        /// <summary>Card lacks a tag required by the Phase's ALL query.</summary>
        MissingAllTag,

        /// <summary>Card has none of the Phase's ANY query tags.</summary>
        MissingAnyTag,

        /// <summary>Card carries a Kink the user marked DontConsent.</summary>
        KinkDontConsent,

        /// <summary>Card carries a Kink with no configured preference (hard-exclude).</summary>
        KinkUnconfigured,

        /// <summary>Card requires Equipment the user does not own.</summary>
        MissingEquipment,

        /// <summary>Card requires a Smart Toy capability that is not available.</summary>
        MissingCapability,
    }

    /// <summary>One machine-readable rejection fact.</summary>
    public sealed class CardRejectionReason
    {
        public CardRejectionReasonKind Kind { get; }
        public string Id { get; }

        public CardRejectionReason(CardRejectionReasonKind kind, string id)
        {
            Kind = kind;
            Id = id ?? "";
        }

        public string Describe()
        {
            switch (Kind)
            {
                case CardRejectionReasonKind.MissingAllTag: return "missing required tag '" + Id + "'";
                case CardRejectionReasonKind.MissingAnyTag: return "has none of the any-of tags (needs '" + Id + "' or another)";
                case CardRejectionReasonKind.KinkDontConsent: return "kink '" + Id + "' is not consented to";
                case CardRejectionReasonKind.KinkUnconfigured: return "kink '" + Id + "' has no configured preference";
                case CardRejectionReasonKind.MissingEquipment: return "requires missing equipment '" + Id + "'";
                case CardRejectionReasonKind.MissingCapability: return "requires unavailable smart toy capability '" + Id + "'";
                default: return Kind.ToString();
            }
        }
    }

    /// <summary>Eligibility result for one Card against one selection context.</summary>
    public sealed class CardEligibility
    {
        public CardDefinition Card { get; }
        public bool IsEligible { get; }
        public List<CardRejectionReason> Reasons { get; }

        public CardEligibility(CardDefinition card, bool isEligible, List<CardRejectionReason> reasons)
        {
            Card = card;
            IsEligible = isEligible;
            Reasons = reasons ?? new List<CardRejectionReason>();
        }
    }

    /// <summary>
    /// The user-side selection state the pipeline evaluates against. Supplied
    /// by the host from the persistent UserProfile (WPF) or tests.
    /// </summary>
    public sealed class CardSelectionProfile
    {
        /// <summary>Kink id -> preference; missing entry means Unconfigured.</summary>
        public Dictionary<string, KinkPreference> KinkPreferences { get; } = new Dictionary<string, KinkPreference>();

        public HashSet<string> OwnedEquipmentIds { get; } = new HashSet<string>();

        public HashSet<string> AvailableCapabilityIds { get; } = new HashSet<string>();

        public static CardSelectionProfile FromProfile(UserProfileSnapshot profile)
        {
            var result = new CardSelectionProfile();
            if (profile == null) return result;

            foreach (var pair in profile.KinkPreferences)
            {
                result.KinkPreferences[pair.Key] = pair.Value;
            }
            foreach (var id in profile.OwnedEquipmentIds) result.OwnedEquipmentIds.Add(id);
            foreach (var id in profile.AvailableCapabilityIds) result.AvailableCapabilityIds.Add(id);
            return result;
        }
    }

    /// <summary>Love/Like/Torture/DontConsent; absence = Unconfigured (not a fifth value).</summary>
    public enum KinkPreference
    {
        Love,
        Like,
        Torture,
        DontConsent,
    }

    /// <summary>
    /// Ticket 07: ordered eligibility pipeline over a Phase's Card query,
    /// consent/Kink configuration, Equipment, and Smart Toy capabilities.
    /// Every listed requirement is required; DontConsent and Unconfigured
    /// Kinks always reject. Pure function of inputs — deterministic.
    /// </summary>
    public static class CardEligibilityEngine
    {
        public static List<CardEligibility> Evaluate(
            IReadOnlyList<CardDefinition> candidates,
            PhaseDefinition phase,
            CardSelectionProfile profile)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (phase == null) throw new ArgumentNullException(nameof(phase));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var result = new List<CardEligibility>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                result.Add(EvaluateOne(candidates[i], phase, profile));
            }
            return result;
        }

        public static CardEligibility EvaluateOne(CardDefinition card, PhaseDefinition phase, CardSelectionProfile profile)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));

            var reasons = new List<CardRejectionReason>();

            // 1. Phase Card tag query (ALL then ANY; empty = no restriction).
            if (phase.MustHaveAllCardTags.Count > 0)
            {
                for (var i = 0; i < phase.MustHaveAllCardTags.Count; i++)
                {
                    var required = phase.MustHaveAllCardTags[i];
                    if (!card.CardTagIds.Contains(required))
                    {
                        reasons.Add(new CardRejectionReason(CardRejectionReasonKind.MissingAllTag, required));
                    }
                }
            }
            if (phase.MustHaveAnyCardTags.Count > 0)
            {
                var any = false;
                for (var i = 0; i < phase.MustHaveAnyCardTags.Count; i++)
                {
                    if (card.CardTagIds.Contains(phase.MustHaveAnyCardTags[i]))
                    {
                        any = true;
                        break;
                    }
                }
                if (!any)
                {
                    reasons.Add(new CardRejectionReason(CardRejectionReasonKind.MissingAnyTag, phase.MustHaveAnyCardTags[0]));
                }
            }

            // 2. Consent / Kink configuration (hard exclusions).
            for (var i = 0; i < card.KinkIds.Count; i++)
            {
                var kinkId = card.KinkIds[i];
                if (!profile.KinkPreferences.TryGetValue(kinkId, out var preference))
                {
                    reasons.Add(new CardRejectionReason(CardRejectionReasonKind.KinkUnconfigured, kinkId));
                    continue;
                }
                if (preference == KinkPreference.DontConsent)
                {
                    reasons.Add(new CardRejectionReason(CardRejectionReasonKind.KinkDontConsent, kinkId));
                }
            }

            // 3. Equipment (all required items must be owned).
            for (var i = 0; i < card.RequiredEquipmentIds.Count; i++)
            {
                var equipmentId = card.RequiredEquipmentIds[i];
                if (!profile.OwnedEquipmentIds.Contains(equipmentId))
                {
                    reasons.Add(new CardRejectionReason(CardRejectionReasonKind.MissingEquipment, equipmentId));
                }
            }

            // 4. Smart Toy capabilities (all required must be available).
            for (var i = 0; i < card.RequiredCapabilityIds.Count; i++)
            {
                var capabilityId = card.RequiredCapabilityIds[i];
                if (!profile.AvailableCapabilityIds.Contains(capabilityId))
                {
                    reasons.Add(new CardRejectionReason(CardRejectionReasonKind.MissingCapability, capabilityId));
                }
            }

            return new CardEligibility(card, reasons.Count == 0, reasons);
        }
    }
}
