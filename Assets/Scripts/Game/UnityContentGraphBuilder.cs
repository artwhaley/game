using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame
{
    /// <summary>
    /// Collects a Unity-authored Session + CardDeck into one portable
    /// GameContentDefinition reference graph. Every referenced Phase, Card,
    /// Action and Resource is converted exactly once and indexed by stable ID:
    /// the same ScriptableObject referenced twice is converted once, while two
    /// distinct assets claiming the same ID fail loudly. Cutscene actions
    /// register their Timeline bindings through the injected registry and
    /// produce portable Resource rows (kind 'cutscene').
    ///
    /// v2 shape: configured actions are gone — every occurrence is an owned
    /// ActionInstanceDefinition inside its sequence. Cards convert their
    /// actions to instances; phases build the standard executable graph;
    /// sessions build the composition graph over PhaseReferences.
    ///
    /// Temporary bridge while Unity runs on an in-memory snapshot built from
    /// ScriptableObjects; a later Unity SQLite adapter loads the same DB into
    /// the same portable snapshot.
    /// </summary>
    public sealed class UnityContentGraphBuilder
    {
        private readonly CutsceneBindingRegistry _registry;

        private readonly Dictionary<string, Phase> _phaseSources = new Dictionary<string, Phase>();
        private readonly Dictionary<string, Card> _cardSources = new Dictionary<string, Card>();
        private readonly Dictionary<string, string> _resourceIds = new Dictionary<string, string>();
        private readonly HashSet<string> _cardTagIds = new HashSet<string>();

        public List<PhaseDefinition> Phases { get; } = new List<PhaseDefinition>();
        public List<CardDefinition> Cards { get; } = new List<CardDefinition>();
        public List<ResourceDefinition> Resources { get; } = new List<ResourceDefinition>();
        public List<CardTagDefinition> CardTags { get; } = new List<CardTagDefinition>();

        public UnityContentGraphBuilder(CutsceneBindingRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public GameContentDefinition Build(Session session, CardDeck deck)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (deck == null) throw new ArgumentNullException(nameof(deck));

            foreach (var card in deck.Cards)
                CollectCard(card);

            return new GameContentDefinition
            {
                SessionTypes =
                {
                    new SessionTypeDefinition { Id = "type-standard", Title = "Standard" },
                },
                Sessions = { session.ToDefinition(this) },
                Phases = Phases,
                Cards = Cards,
                Resources = Resources,
                CardTagDefinitions = CardTags,
            };
        }

        public void CollectPhase(Phase phase)
        {
            if (phase == null) return;
            phase.EnsureId();
            if (_phaseSources.TryGetValue(phase.Id, out var existing))
            {
                if (!ReferenceEquals(existing, phase))
                {
                    throw new InvalidOperationException(
                        $"[TruthCardGame] Duplicate Phase id '{phase.Id}' across distinct assets. Rename one.");
                }
                return;
            }
            _phaseSources[phase.Id] = phase;
            var definition = phase.ToDefinition();
            Phases.Add(definition);
            CollectCardTags(definition.MustHaveAllCardTags);
            CollectCardTags(definition.MustHaveAnyCardTags);
        }

        public void CollectCard(Card card)
        {
            if (card == null) return;
            card.EnsureId();
            if (_cardSources.TryGetValue(card.Id, out var existing))
            {
                if (!ReferenceEquals(existing, card))
                {
                    throw new InvalidOperationException(
                        $"[TruthCardGame] Duplicate Card id '{card.Id}' across distinct assets. Rename one.");
                }
                return;
            }
            _cardSources[card.Id] = card;
            var definition = card.ToDefinition(this);
            Cards.Add(definition);
            CollectCardTags(definition.CardTagIds);
        }

        private void CollectCardTags(IEnumerable<string> tagIds)
        {
            if (tagIds == null) return;
            foreach (var tagId in tagIds)
            {
                if (string.IsNullOrEmpty(tagId) || !_cardTagIds.Add(tagId)) continue;
                CardTags.Add(new CardTagDefinition { Id = tagId, Title = tagId });
            }
        }

        /// <summary>
        /// Binds the cutscene's timeline under its stable resource id
        /// (minting one if absent) and collects the portable Resource row.
        /// Registering the same id twice fails loudly.
        /// </summary>
        public void CollectCutscene(CutsceneAction action)
        {
            if (action == null) return;

            // A timeline can only bind under a stable id; mint one if the
            // author left it empty.
            if (action.HasTimeline)
            {
                action.EnsureResourceId();
                _registry.Register(action.ResourceId, action.Timeline);
            }

            // Declare the resource whenever the converted instance carries an
            // id, even with no timeline assigned yet. Sample content ships its
            // authored id ('cs:intro') before the TimelineAsset is authored by
            // hand, and ToDefinition still emits an instance referencing it —
            // an undeclared reference fails ContentReferenceValidator at
            // engine construction, which blocks *every* session rather than
            // the one card. Unbound, playback logs the missing-cutscene error
            // at the card's authored moment instead (DirectorPlayer).
            if (string.IsNullOrEmpty(action.ResourceId)) return;
            if (_resourceIds.ContainsKey(action.ResourceId)) return;

            _resourceIds[action.ResourceId] = action.ResourceId;
            Resources.Add(new ResourceDefinition
            {
                Id = action.ResourceId,
                Kind = "cutscene",
                Name = action.name
            });
        }
    }
}
