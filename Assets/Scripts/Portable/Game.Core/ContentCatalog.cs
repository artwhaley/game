using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Indexes the independent entities of a GameContentDefinition snapshot by
    /// stable ID (Session, Phase, Card, Resource, SessionType, Temperature).
    /// Construction rejects duplicate IDs and missing/empty IDs so content errors
    /// surface at session start. Typed lookups fail loudly with context — a
    /// required missing reference is never silently tolerated.
    ///
    /// This is a runtime resolution seam, not a database or editor model: no
    /// mutation, no save logic.
    /// </summary>
    public sealed class ContentCatalog
    {
        private readonly Dictionary<string, SessionDefinition> _sessions;
        private readonly Dictionary<string, PhaseDefinition> _phases;
        private readonly Dictionary<string, CardDefinition> _cards;
        private readonly Dictionary<string, ResourceDefinition> _resources;
        private readonly Dictionary<string, SessionTypeDefinition> _sessionTypes;
        private readonly Dictionary<string, TemperatureDefinition> _temperatures;
        private readonly IReadOnlyList<TemperatureDefinition> _temperaturesList;

        public ContentCatalog(GameContentDefinition content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (content.Deck == null) throw new InvalidOperationException("ContentCatalog: content.Deck must not be null.");

            _sessions = Index(content.Sessions, "Session", s => s.Id);
            _phases = Index(content.Phases, "Phase", p => p.Id);
            _cards = Index(content.Cards, "Card", c => c.Id);
            _resources = Index(content.Resources, "Resource", r => r.Id);
            _sessionTypes = Index(content.SessionTypes, "SessionType", t => t.Id);
            _temperatures = Index(content.Temperatures, "Temperature", t => t.Id);
            _temperaturesList = content.Temperatures == null
                ? (IReadOnlyList<TemperatureDefinition>)new List<TemperatureDefinition>()
                : content.Temperatures;
        }

        private static Dictionary<string, T> Index<T>(IReadOnlyList<T> entities, string typeName, Func<T, string> idOf) where T : class
        {
            var index = new Dictionary<string, T>();
            if (entities == null) return index;

            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity == null)
                {
                    throw new InvalidOperationException($"ContentCatalog: null {typeName} entry at index {i}.");
                }

                var id = idOf(entity);
                if (string.IsNullOrEmpty(id))
                {
                    throw new InvalidOperationException($"ContentCatalog: {typeName} at index {i} has a missing/empty id.");
                }

                if (index.ContainsKey(id))
                {
                    throw new InvalidOperationException($"ContentCatalog: duplicate {typeName} id '{id}'.");
                }

                index.Add(id, entity);
            }

            return index;
        }

        public SessionDefinition SessionById(string id)
        {
            return Lookup(_sessions, "Session", id);
        }

        public PhaseDefinition PhaseById(string id)
        {
            return Lookup(_phases, "Phase", id);
        }

        public CardDefinition CardById(string id)
        {
            return Lookup(_cards, "Card", id);
        }

        public ResourceDefinition ResourceById(string id)
        {
            return Lookup(_resources, "Resource", id);
        }

        /// <summary>Silently absent semantics — used by optional-reference seams like SessionSelector (Ticket 06).</summary>
        public bool TrySessionById(string id, out SessionDefinition session)
        {
            return _sessions.TryGetValue(id ?? "", out session);
        }

        public SessionTypeDefinition SessionTypeById(string id)
        {
            return Lookup(_sessionTypes, "SessionType", id);
        }

        public TemperatureDefinition TemperatureById(string id)
        {
            return Lookup(_temperatures, "Temperature", id);
        }

        /// <summary>All temperature definitions in stable order (TemperatureState initialization).</summary>
        public IReadOnlyList<TemperatureDefinition> TemperaturesList => _temperaturesList;

        private static T Lookup<T>(Dictionary<string, T> index, string typeName, string id) where T : class
        {
            if (!index.TryGetValue(id ?? "", out var value))
            {
                throw new InvalidOperationException($"ContentCatalog: no {typeName} with id '{id}'.");
            }
            return value;
        }
    }
}
