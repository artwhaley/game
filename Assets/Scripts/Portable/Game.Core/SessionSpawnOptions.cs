using System;
using System.Collections.Generic;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Session initialization contact (Ticket 06). Replaces ad-hoc constructor
    /// state: a host starts a session with these options and nothing else.
    /// Profile/device requirements live OUTSIDE this type — profile code filters
    /// eligible sessions via SessionSelector's eligibility predicate before
    /// spawning; this type never reads a profile DB.
    /// </summary>
    public sealed class SessionSpawnOptions
    {
        /// <summary>
        /// Per-Temperature start overrides keyed by stable Temperature id.
        /// Absent ids keep their definition default (Happiness 50 when unset).
        /// Unknown ids are a content error and fail loudly at spawn.
        /// </summary>
        public Dictionary<string, float> TemperatureOverrides { get; } = new Dictionary<string, float>();

        public static readonly SessionSpawnOptions Default = new SessionSpawnOptions();

        public SessionSpawnOptions OverrideTemperature(string temperatureId, float value)
        {
            TemperatureOverrides[temperatureId] = value;
            return this;
        }

        /// <summary>
        /// Convenience for the Happiness temperature (contract: session spawn
        /// defaults to 50; hosts may override for test/diagnostic purposes.
        /// Session definitions cannot author a start value).
        /// </summary>
        public SessionSpawnOptions OverrideHappiness(float value)
        {
            return OverrideTemperature(PhaseGraphVm.HappinessTemperatureId, value);
        }

        public bool TryGetTemperatureOverride(string temperatureId, out float value)
        {
            return TemperatureOverrides.TryGetValue(temperatureId ?? "", out value);
        }
    }
}
