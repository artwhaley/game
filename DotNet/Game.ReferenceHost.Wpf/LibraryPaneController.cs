using System;
using System.Collections.Generic;
using System.Linq;
using TruthCardGame.Content;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>State and filtering policy for the shared Session/Phase library.</summary>
    internal sealed class LibraryPaneController
    {
        public bool IsSessionMode { get; private set; } = true;
        public string SessionUsageFilterPhaseId { get; private set; }

        public void ShowSessions() => IsSessionMode = true;
        public void ShowPhases() => IsSessionMode = false;

        public void FilterToSessionsUsing(PhaseDefinition phase)
        {
            IsSessionMode = true;
            SessionUsageFilterPhaseId = phase?.Id;
        }

        public void ClearSessionUsageFilter() => SessionUsageFilterPhaseId = null;

        public List<SessionDefinition> FilterSessions(GameContentDefinition content, string filter)
        {
            var sessions = content?.Sessions ?? new List<SessionDefinition>();
            var query = (filter ?? "").Trim();
            var result = string.IsNullOrEmpty(query)
                ? sessions.ToList()
                : sessions.Where(session => (session.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (!string.IsNullOrEmpty(SessionUsageFilterPhaseId))
            {
                result = result.Where(session => session.Graph != null && session.Graph.Nodes.OfType<PhaseReferenceNodeDefinition>()
                    .Any(reference => reference.PhaseId == SessionUsageFilterPhaseId)).ToList();
            }
            return result;
        }

        public List<PhaseDefinition> FilterPhases(GameContentDefinition content, string filter)
        {
            var phases = content?.Phases ?? new List<PhaseDefinition>();
            var query = (filter ?? "").Trim();
            return string.IsNullOrEmpty(query)
                ? phases.ToList()
                : phases.Where(phase => (phase.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
    }
}
