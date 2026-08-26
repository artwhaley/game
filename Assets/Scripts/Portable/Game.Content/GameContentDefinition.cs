using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// Portable in-memory content library root: the full reference graph Core
    /// executes against. Entities (Deck, Sessions, Phases, Cards, Actions,
    /// Resources) are top-level and referenced by opaque stable ID; owned
    /// records (PhaseSlots, ChoiceOptions) live inside their parent.
    ///
    /// This is a snapshot, not a durable store. SQLite rows/foreign keys are
    /// the canonical persistence; this definition is the loaded projection.
    /// </summary>
    public sealed class GameContentDefinition
    {
        public CardDeckDefinition Deck { get; set; } = new CardDeckDefinition();
        public List<SessionDefinition> Sessions { get; set; } = new List<SessionDefinition>();
        public List<PhaseDefinition> Phases { get; set; } = new List<PhaseDefinition>();
        public List<CardDefinition> Cards { get; set; } = new List<CardDefinition>();
        public List<GameActionDefinition> Actions { get; set; } = new List<GameActionDefinition>();
        public List<ResourceDefinition> Resources { get; set; } = new List<ResourceDefinition>();
    }
}
