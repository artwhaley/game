using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Ticket 10: SessionType-level eligibility + uniform Session selection.
    /// A SessionType is eligible iff every required Smart Toy capability is
    /// available. Selection is uniform among Sessions of the chosen type via
    /// a DEDICATED selection RNG — never the PhaseRun card RNG.
    /// </summary>
    public sealed class SessionTypeEligibility
    {
        private readonly ContentCatalog _catalog;

        public SessionTypeEligibility(ContentCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Typed eligibility result with the missing capability ids.</summary>
        public sealed class Result
        {
            public SessionTypeDefinition SessionType { get; }
            public bool IsEligible { get; }
            public List<string> MissingCapabilityIds { get; }

            public Result(SessionTypeDefinition type, bool isEligible, List<string> missing)
            {
                SessionType = type;
                IsEligible = isEligible;
                MissingCapabilityIds = missing ?? new List<string>();
            }
        }

        public Result Evaluate(SessionTypeDefinition type, CardSelectionProfile profile)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var missing = new List<string>();
            for (var i = 0; i < type.RequiredCapabilityIds.Count; i++)
            {
                var capabilityId = type.RequiredCapabilityIds[i];
                if (!profile.AvailableCapabilityIds.Contains(capabilityId))
                {
                    missing.Add(capabilityId);
                }
            }

            return new Result(type, missing.Count == 0, missing);
        }

        public Result Evaluate(string sessionTypeId, CardSelectionProfile profile)
        {
            return Evaluate(_catalog.SessionTypeById(sessionTypeId), profile);
        }

        /// <summary>Uniform random Session among Sessions of the given type; type capability requirements gate eligibility.</summary>
        public bool TrySelectSession(
            string sessionTypeId,
            CardSelectionProfile profile,
            IRandomSource selectionRng,
            out SessionDefinition selected)
        {
            selected = null;

            // Loud: an unknown type is a content error, never silently empty.
            var type = _catalog.SessionTypeById(sessionTypeId);

            // Type-level capability requirements gate every session of that type.
            var eligibility = Evaluate(type, profile);
            if (!eligibility.IsEligible)
            {
                return false;
            }

            var candidates = new List<SessionDefinition>();
            foreach (var session in _catalog.SessionsList)
            {
                if (session.SessionTypeId != type.Id) continue;
                candidates.Add(session);
            }

            if (candidates.Count == 0) return false;

            selected = candidates[selectionRng.NextInt(0, candidates.Count)];
            return true;
        }
    }
}
