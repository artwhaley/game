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
    /// Temporary bridge while Unity runs on an in-memory snapshot built from
    /// ScriptableObjects; a later Unity SQLite adapter loads the same DB into
    /// the same portable snapshot.
    /// </summary>
    public sealed class UnityContentGraphBuilder
    {
        private readonly CutsceneBindingRegistry _registry;

        private readonly Dictionary<string, Phase> _phaseSources = new Dictionary<string, Phase>();
        private readonly Dictionary<string, Card> _cardSources = new Dictionary<string, Card>();
        private readonly Dictionary<string, CardAction> _actionSources = new Dictionary<string, CardAction>();
        private readonly Dictionary<string, string> _resourceIds = new Dictionary<string, string>();

        public List<PhaseDefinition> Phases { get; } = new List<PhaseDefinition>();
        public List<CardDefinition> Cards { get; } = new List<CardDefinition>();
        public List<GameActionDefinition> Actions { get; } = new List<GameActionDefinition>();
        public List<ResourceDefinition> Resources { get; } = new List<ResourceDefinition>();

        public UnityContentGraphBuilder(CutsceneBindingRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public GameContentDefinition Build(Session session, CardDeck deck)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (deck == null) throw new ArgumentNullException(nameof(deck));

            return new GameContentDefinition
            {
                Deck = deck.ToDefinition(this),
                Sessions = { session.ToDefinition(this) },
                Phases = Phases,
                Cards = Cards,
                Actions = Actions,
                Resources = Resources
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
            Phases.Add(phase.ToDefinition());
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
            Cards.Add(card.ToDefinition(this));
        }

        public void CollectAction(CardAction action)
        {
            if (action == null) return;
            action.EnsureId();
            if (_actionSources.TryGetValue(action.Id, out var existing))
            {
                if (!ReferenceEquals(existing, action))
                {
                    throw new InvalidOperationException(
                        $"[TruthCardGame] Duplicate Action id '{action.Id}' across distinct assets. Rename one.");
                }
                return;
            }
            _actionSources[action.Id] = action;
            Actions.Add(action.ToDefinition(this));
        }

        /// <summary>
        /// Binds the cutscene's timeline under its stable resource id
        /// (minting one if absent) and collects the portable Resource row.
        /// Registering the same id twice fails loudly.
        /// </summary>
        public void CollectCutscene(CutsceneAction action)
        {
            if (action == null) return;
            if (!action.HasTimeline) return;

            action.EnsureResourceId();
            _registry.Register(action.ResourceId, action.Timeline);

            if (!_resourceIds.ContainsKey(action.ResourceId))
            {
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
}
