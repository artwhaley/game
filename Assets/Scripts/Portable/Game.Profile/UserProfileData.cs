using System.Collections.Generic;
using TruthCardGame.Core;

namespace TruthCardGame.Profile
{
    /// <summary>
    /// The portable user profile: what the player configured, as opposed to
    /// what the content catalogs define (GameContent.db). Missing rows mean
    /// Unconfigured / not owned / unavailable — never fabricated defaults.
    /// </summary>
    public sealed class UserProfileData
    {
        /// <summary>Kink id -> preference. Absent entry = Unconfigured.</summary>
        public Dictionary<string, KinkPreference> KinkPreferences { get; } = new Dictionary<string, KinkPreference>();

        /// <summary>Equipment ids the user owns. Absent = not owned.</summary>
        public HashSet<string> OwnedEquipmentIds { get; } = new HashSet<string>();

        /// <summary>Smart Toy capability ids available to the user. Absent = unavailable.</summary>
        public HashSet<string> AvailableCapabilityIds { get; } = new HashSet<string>();

        /// <summary>Cached Core-side snapshot for the selection pipeline.</summary>
        public UserProfileSnapshot ToSnapshot()
        {
            var snapshot = new UserProfileSnapshot();
            foreach (var pair in KinkPreferences) snapshot.KinkPreferences[pair.Key] = pair.Value;
            foreach (var id in OwnedEquipmentIds) snapshot.OwnedEquipmentIds.Add(id);
            foreach (var id in AvailableCapabilityIds) snapshot.AvailableCapabilityIds.Add(id);
            return snapshot;
        }
    }
}
