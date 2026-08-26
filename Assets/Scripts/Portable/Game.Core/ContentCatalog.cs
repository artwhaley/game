using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Indexes the independent entities of a GameContentDefinition snapshot by
    /// stable ID (Session, Phase, Card, Action, Resource). Construction
    /// rejects duplicate IDs and missing/empty IDs so content errors surface
    /// at session start. Typed lookups fail loudly with context — a required
    /// missing reference is never silently tolerated.
    ///
    /// This is a runtime resolution seam, not a database or editor model: no
    /// mutation, no save logic.
    /// </summary>
    public sealed class ContentCatalog
    {
        private readonly Dictionary<string, SessionDefinition> _sessions;
        private readonly Dictionary<string, PhaseDefinition> _phases;
        private readonly Dictionary<string, CardDefinition> _cards;
        private readonly Dictionary<string, GameActionDefinition> _actions;
        private readonly Dictionary<string, ResourceDefinition> _resources;

        public ContentCatalog(GameContentDefinition content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (content.Deck == null) throw new InvalidOperationException("ContentCatalog: content.Deck must not be null.");

            _sessions = Index(content.Sessions, "Session");
            _phases = Index(content.Phases, "Phase");
            _cards = Index(content.Cards, "Card");
            _actions = Index(content.Actions, "Action");
            _resources = Index(content.Resources, "Resource");
        }

        private static Dictionary<string, T> Index<T>(IReadOnlyList<T> entities, string typeName) where T : class
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

                var id = GetId(entity);
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

        private static string GetId<T>(T entity) where T : class
        {
            if (entity is SessionDefinition s) return s.Id;
            if (entity is PhaseDefinition p) return p.Id;
            if (entity is CardDefinition c) return c.Id;
            if (entity is GameActionDefinition a) return a.Id;
            if (entity is ResourceDefinition r) return r.Id;
            throw new InvalidOperationException($"ContentCatalog: unsupported entity type {typeof(T).Name}.");
        }

        public SessionDefinition SessionById(string id)
        {
            if (!_sessions.TryGetValue(id ?? "", out var value))
            {
                throw new InvalidOperationException($"ContentCatalog: no Session with id '{id}'.");
            }
            return value;
        }

        public PhaseDefinition PhaseById(string id)
        {
            if (!_phases.TryGetValue(id ?? "", out var value))
            {
                throw new InvalidOperationException($"ContentCatalog: no Phase with id '{id}'.");
            }
            return value;
        }

        public CardDefinition CardById(string id)
        {
            if (!_cards.TryGetValue(id ?? "", out var value))
            {
                throw new InvalidOperationException($"ContentCatalog: no Card with id '{id}'.");
            }
            return value;
        }

        public GameActionDefinition ActionById(string id)
        {
            if (!_actions.TryGetValue(id ?? "", out var value))
            {
                throw new InvalidOperationException($"ContentCatalog: no Action with id '{id}'.");
            }
            return value;
        }

        public ResourceDefinition ResourceById(string id)
        {
            if (!_resources.TryGetValue(id ?? "", out var value))
            {
                throw new InvalidOperationException($"ContentCatalog: no Resource with id '{id}'.");
            }
            return value;
        }
    }
}
