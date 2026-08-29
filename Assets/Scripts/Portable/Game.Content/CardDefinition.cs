using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// One authored Card: presentation text, classification tags, kink/equipment/
    /// capability requirements, and exactly one owned Action sequence. There are
    /// no reusable configured Action entities and no cross-card ID references —
    /// every occurrence is this card's own instance with its own values. Pacing
    /// and progress are authored Action Instances; the runtime never inserts
    /// hidden actions into a Card.
    /// </summary>
    public sealed class CardDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string BodyText { get; set; } = "";
        /// <summary>Optional slash-delimited authoring folder path; runtime semantics ignore it.</summary>
        public string FolderPath { get; set; } = "";

        /// <summary>CardTagDefinition ids evaluated against a Phase's ALL/ANY query.</summary>
        public List<string> CardTagIds { get; set; } = new List<string>();

        /// <summary>KinkDefinition ids carried by this Card.</summary>
        public List<string> KinkIds { get; set; } = new List<string>();

        /// <summary>Every listed EquipmentDefinition is required to play this Card.</summary>
        public List<string> RequiredEquipmentIds { get; set; } = new List<string>();

        /// <summary>Every listed SmartToyCapabilityDefinition is required to play this Card.</summary>
        public List<string> RequiredCapabilityIds { get; set; } = new List<string>();

        /// <summary>This card's owned sequence of Action Instances.</summary>
        public ActionSequenceDefinition Sequence { get; set; } = new ActionSequenceDefinition();
    }
}
