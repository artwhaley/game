using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// The fully in-memory game content snapshot. Loaded from the canonical SQLite
    /// store (Game.Content.Sqlite) and executed by Game.Core; never mutated by the
    /// runtime during a run and never queried from SQL while running.
    ///
    /// Configured Actions are NOT entities here anymore: every occurrence is an
    /// owned ActionInstanceDefinition living inside the sequence that contains it.
    /// PhaseSlots are gone entirely — Sessions compose reusable Phases directly.
    /// </summary>
    public sealed class GameContentDefinition
    {
        public CardDeckDefinition Deck { get; set; } = new CardDeckDefinition();

        /// <summary>Authored Session categories; Sessions reference exactly one.</summary>
        public List<SessionTypeDefinition> SessionTypes { get; set; } = new List<SessionTypeDefinition>();

        /// <summary>Authored Temperature definitions (e.g. Happiness 0..100 default 50).</summary>
        public List<TemperatureDefinition> Temperatures { get; set; } = new List<TemperatureDefinition>();

        public List<SessionDefinition> Sessions { get; set; } = new List<SessionDefinition>();
        public List<PhaseDefinition> Phases { get; set; } = new List<PhaseDefinition>();
        public List<CardDefinition> Cards { get; set; } = new List<CardDefinition>();
        public List<ResourceDefinition> Resources { get; set; } = new List<ResourceDefinition>();
    }
}
