using System.Collections.Generic;

namespace TruthCardGame.Content.Json
{
    /// <summary>
    /// Top-level portable content document (schemaVersion 1): one deck plus
    /// ordered sessions. Sessions embed phases; cards live in the deck;
    /// choice children nest inline. No stable IDs yet by design.
    /// </summary>
    public sealed class ContentDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public CardDeckDefinition Deck { get; set; } = new CardDeckDefinition();
        public List<SessionDefinition> Sessions { get; set; } = new List<SessionDefinition>();
    }
}
