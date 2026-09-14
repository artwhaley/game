using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// What an executing Action can see and mutate at runtime: the player, host
    /// services, session temperatures, the active PhaseRun's progress (null
    /// when no PhaseRun is active), and the owner scope of the sequence being
    /// executed (registry scope guard).
    /// </summary>
    public sealed class ActionExecutionContext
    {
        public Player Player { get; }
        public CoreServices Services { get; }
        public ContentCatalog Catalog { get; }

        /// <summary>Session-global temperature values keyed by stable Temperature id.</summary>
        public TemperatureState Temperatures { get; }

        /// <summary>Active PhaseRun progress; null when executing with no active PhaseRun (session-level).</summary>
        public PhaseProgressState PhaseProgress { get; }

        /// <summary>Owner scope of the sequence being executed (registry guard).</summary>
        public ActionOwnerScope ActiveScope { get; }

        /// <summary>
        /// Session-scoped Dialog From Tags RNG (Ticket 05, domain
        /// DialogSelection). Independent from Session selection and the
        /// PhaseRun/Card streams; nested Action contexts share the same
        /// instance so dialog draws stay one ordered session stream. Optional:
        /// null only in hosts/tests that never execute DialogFromTags.
        /// </summary>
        public IRandomSource DialogRng { get; }

        /// <summary>
        /// Session-scoped performance director, when the run supplies a
        /// performance host. Ordinary actions never require it; Perform and the
        /// blocking-dialogue refresh use it when present.
        /// </summary>
        public PerformanceDirector Performance { get; }

        public ActionExecutionContext(
            Player player,
            CoreServices services,
            ContentCatalog catalog,
            TemperatureState temperatures,
            PhaseProgressState phaseProgress,
            ActionOwnerScope activeScope,
            IRandomSource dialogRng = null,
            PerformanceDirector performance = null)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Temperatures = temperatures ?? throw new ArgumentNullException(nameof(temperatures));
            PhaseProgress = phaseProgress;
            ActiveScope = activeScope;
            DialogRng = dialogRng;
            Performance = performance;
        }
    }

    /// <summary>
    /// Mutable holder for the active PhaseRun's progress so action execution
    /// (IncrementProgress) can adjust it; the PhaseRun object itself lands with
    /// the graph VM (Ticket 07).
    /// </summary>
    public sealed class PhaseProgressState
    {
        public float Value { get; set; }
    }

    /// <summary>
    /// Session-global temperatures. Values keyed by stable Temperature id and
    /// initialized from the definitions' defaults (with optional spawn
    /// overrides applied on top); mutations clamp to each definition's bounds.
    /// Missing definitions are a runtime error, never silently tolerated.
    /// </summary>
    public sealed class TemperatureState
    {
        private readonly Dictionary<string, float> _values = new Dictionary<string, float>();
        private readonly ContentCatalog _catalog;

        public TemperatureState(ContentCatalog catalog)
            : this(catalog, SessionSpawnOptions.Default)
        {
        }

        public TemperatureState(ContentCatalog catalog, SessionSpawnOptions spawn)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            spawn = spawn ?? SessionSpawnOptions.Default;

            foreach (var definition in catalog.TemperaturesList)
            {
                _values[definition.Id] = definition.DefaultValue;
            }

            // Spawn overrides replace the default per-value; unknown ids are a
            // content error (never silently ignored).
            foreach (var pair in spawn.TemperatureOverrides)
            {
                var definition = catalog.TemperatureById(pair.Key);
                _values[pair.Key] = Clamp(pair.Value, definition.MinValue, definition.MaxValue);
            }
        }

        public float Get(string temperatureId)
        {
            if (!_values.TryGetValue(temperatureId, out var value))
            {
                throw new InvalidOperationException(
                    $"TemperatureState: no temperature '{temperatureId}' is defined for this session.");
            }
            return value;
        }

        /// <summary>Adds a delta, clamped to the definition's [MinValue, MaxValue].</summary>
        public void Add(string temperatureId, float amount)
        {
            var definition = _catalog.TemperatureById(temperatureId);
            var current = _values.TryGetValue(temperatureId, out var value) ? value : definition.DefaultValue;
            var next = current + amount;
            _values[temperatureId] = Clamp(next, definition.MinValue, definition.MaxValue);
        }

        public void Set(string temperatureId, float value)
        {
            var definition = _catalog.TemperatureById(temperatureId);
            _values[temperatureId] = Clamp(value, definition.MinValue, definition.MaxValue);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
