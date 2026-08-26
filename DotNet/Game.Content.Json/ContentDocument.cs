using System.Collections.Generic;

namespace TruthCardGame.Content.Json
{
    /// <summary>
    /// Top-level portable content document (schemaVersion 2): one deck plus
    /// ordered sessions. Sessions embed phases; cards live in the deck;
    /// choice children nest inline. Every entity carries a stable GUID "id"
    /// minted once at authoring time — names and positions may change, ids
    /// never do, so cross-host references (e.g. Unity cutscene bindings) stay
    /// stable across authoring round trips.
    /// </summary>
    public sealed class ContentDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public CardDeckDefinition Deck { get; set; } = new CardDeckDefinition();
        public List<SessionDefinition> Sessions { get; set; } = new List<SessionDefinition>();
    }
}
