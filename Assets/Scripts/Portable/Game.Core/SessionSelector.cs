using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Chooses a Session to spawn: filters sessions by SessionType and an
    /// optional eligibility predicate (future profile/device code supplies it),
    /// then selects uniformly with a DEDICATED selection RNG — never the
    /// runtime Phase card RNG. Unknown SessionTypes fail loudly.
    /// </summary>
    public sealed class SessionSelector
    {
        private readonly ContentCatalog _catalog;

        public SessionSelector(ContentCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public bool TrySelect(
            string sessionTypeId,
            IRandomSource selectionRng,
            out SessionDefinition selected,
            Func<SessionDefinition, bool> eligible = null)
        {
            selected = null;

            // Loud, not silent: a launcher referencing a missing type is content error.
            _catalog.SessionTypeById(sessionTypeId);

            var candidates = new List<SessionDefinition>();
            foreach (var session in _catalog.SessionsList)
            {
                if (session.SessionTypeId != sessionTypeId) continue;
                if (eligible != null && !eligible(session)) continue;
                candidates.Add(session);
            }

            if (candidates.Count == 0) return false;

            selected = candidates[selectionRng.NextInt(0, candidates.Count)];
            return true;
        }
    }
}
