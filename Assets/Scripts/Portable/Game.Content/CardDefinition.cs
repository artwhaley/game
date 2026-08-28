using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// One authored Card: presentation metadata, eligibility tags, and exactly one
    /// owned Action sequence. There are no reusable configured Action entities and
    /// no cross-card ID references — every occurrence is this card's own instance
    /// with its own values. Pacing and progress are authored Action Instances;
    /// the runtime never inserts hidden actions into a Card.
    /// </summary>
    public sealed class CardDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Eligibility tags evaluated against a Phase's must-include/must-exclude lists.</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>This card's owned sequence of Action Instances.</summary>
        public ActionSequenceDefinition Sequence { get; set; } = new ActionSequenceDefinition();
    }
}
