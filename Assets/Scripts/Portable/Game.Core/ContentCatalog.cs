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
        private readonly Dictionary<string, DialogTagDefinition> _dialogTags;
        private readonly Dictionary<string, DialogSnippetDefinition> _dialogSnippets;
        private readonly Dictionary<string, PerformanceTagDefinition> _performanceTags;
        private readonly Dictionary<string, ConversationPerformanceEventDefinition> _performanceEvents;
        private readonly IReadOnlyList<PerformanceTagDefinition> _performanceTagsList;
        private readonly IReadOnlyList<ConversationPerformanceEventDefinition> _performanceEventsList;
        private readonly IReadOnlyList<TemperatureDefinition> _temperaturesList;
        private readonly IReadOnlyList<SessionDefinition> _sessionsList;
        private readonly IReadOnlyList<DialogTagDefinition> _dialogTagsList;
        private readonly IReadOnlyList<DialogSnippetDefinition> _dialogSnippetsList;

        public ContentCatalog(GameContentDefinition content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            _sessions = Index(content.Sessions, "Session", s => s.Id);
            _phases = Index(content.Phases, "Phase", p => p.Id);
            _cards = Index(content.Cards, "Card", c => c.Id);
            _resources = Index(content.Resources, "Resource", r => r.Id);
            _sessionTypes = Index(content.SessionTypes, "SessionType", t => t.Id);
            _temperatures = Index(content.Temperatures, "Temperature", t => t.Id);
            _dialogTags = Index(content.DialogTags, "DialogTag", t => t.Id);
            _dialogSnippets = Index(content.DialogSnippets, "DialogSnippet", s => s.Id);
            _performanceTags = Index(content.PerformanceTags, "PerformanceTag", t => t.Id);
            _performanceEvents = Index(content.PerformanceEvents, "PerformanceEvent", e => e.Id);
            _performanceTagsList = content.PerformanceTags == null
                ? (IReadOnlyList<PerformanceTagDefinition>)new List<PerformanceTagDefinition>()
                : content.PerformanceTags;
            _performanceEventsList = content.PerformanceEvents == null
                ? (IReadOnlyList<ConversationPerformanceEventDefinition>)new List<ConversationPerformanceEventDefinition>()
                : content.PerformanceEvents;
            _temperaturesList = content.Temperatures == null
                ? (IReadOnlyList<TemperatureDefinition>)new List<TemperatureDefinition>()
                : content.Temperatures;
            _sessionsList = content.Sessions == null
                ? (IReadOnlyList<SessionDefinition>)new List<SessionDefinition>()
                : content.Sessions;
            _dialogTagsList = content.DialogTags == null
                ? (IReadOnlyList<DialogTagDefinition>)new List<DialogTagDefinition>()
                : content.DialogTags;
            _dialogSnippetsList = content.DialogSnippets == null
                ? (IReadOnlyList<DialogSnippetDefinition>)new List<DialogSnippetDefinition>()
                : content.DialogSnippets;
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

        /// <summary>All sessions in stable order (SessionSelector filtering).</summary>
        public IReadOnlyList<SessionDefinition> SessionsList => _sessionsList;

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

        public DialogTagDefinition DialogTagById(string id)
        {
            return Lookup(_dialogTags, "DialogTag", id);
        }

        public bool TryDialogTagById(string id, out DialogTagDefinition tag)
        {
            return _dialogTags.TryGetValue(id ?? "", out tag);
        }
        /// <summary>All Dialog Tags in stable order (WPF catalog + selector diagnostics).</summary>
        public IReadOnlyList<DialogTagDefinition> DialogTagsList => _dialogTagsList;

        public DialogSnippetDefinition DialogSnippetById(string id)
        {
            return Lookup(_dialogSnippets, "DialogSnippet", id);
        }

        /// <summary>All Dialog Snippets in stable order (DialogFromTags selection input).</summary>
        public IReadOnlyList<DialogSnippetDefinition> DialogSnippetsList => _dialogSnippetsList;

        public PerformanceTagDefinition PerformanceTagById(string id)
        {
            return Lookup(_performanceTags, "PerformanceTag", id);
        }

        public bool TryPerformanceTagById(string id, out PerformanceTagDefinition tag)
        {
            return _performanceTags.TryGetValue(id ?? "", out tag);
        }

        /// <summary>All Performance Tags in stable authoring order (Unity picker + diagnostics).</summary>
        public IReadOnlyList<PerformanceTagDefinition> PerformanceTagsList => _performanceTagsList;

        public ConversationPerformanceEventDefinition PerformanceEventById(string id)
        {
            return Lookup(_performanceEvents, "PerformanceEvent", id);
        }

        /// <summary>All Conversation Performance Events in stable authoring order.</summary>
        public IReadOnlyList<ConversationPerformanceEventDefinition> PerformanceEventsList => _performanceEventsList;

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
