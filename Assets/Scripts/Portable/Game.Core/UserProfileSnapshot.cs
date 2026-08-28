using System;
using System.Collections.Generic;
using System.Text;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// The persistent user profile snapshot (portable side). Game.Profile
    /// owns persistence; Core only consumes the resolved values.
    /// Missing kink row = Unconfigured; missing equipment/capability = unavailable.
    /// </summary>
    public sealed class UserProfileSnapshot
    {
        public Dictionary<string, KinkPreference> KinkPreferences { get; } = new Dictionary<string, KinkPreference>();
        public HashSet<string> OwnedEquipmentIds { get; } = new HashSet<string>();
        public HashSet<string> AvailableCapabilityIds { get; } = new HashSet<string>();
    }
}
