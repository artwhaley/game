using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// The fully in-memory game content snapshot. Loaded from the canonical SQLite
    /// store (Game.Content.Sqlite) and executed by Game.Core; never mutated by the
    /// runtime during a run and never queried from SQL while running.
    ///
    /// Configured Actions are NOT entities here: every occurrence is an owned
    /// ActionInstanceDefinition living inside the sequence that contains it.
    /// PhaseSlots and the deck concept are gone — Sessions compose reusable
    /// Phases directly, and Card selection is the Phase Card query + eligibility
    /// + weighting pipeline.
    /// </summary>
    public sealed class GameContentDefinition
    {
        /// <summary>Authored Session categories; Sessions reference exactly one.</summary>
        public List<SessionTypeDefinition> SessionTypes { get; set; } = new List<SessionTypeDefinition>();

        /// <summary>Authored Temperature definitions (e.g. Happiness 0..100 default 50).</summary>
        public List<TemperatureDefinition> Temperatures { get; set; } = new List<TemperatureDefinition>();

        /// <summary>Authored Card classification tags.</summary>
        public List<CardTagDefinition> CardTagDefinitions { get; set; } = new List<CardTagDefinition>();

        /// <summary>Authored Kink definitions.</summary>
        public List<KinkDefinition> KinkDefinitions { get; set; } = new List<KinkDefinition>();

        /// <summary>Authored Equipment definitions.</summary>
        public List<EquipmentDefinition> EquipmentDefinitions { get; set; } = new List<EquipmentDefinition>();

        /// <summary>Authored Smart Toy capability definitions.</summary>
        public List<SmartToyCapabilityDefinition> SmartToyCapabilityDefinitions { get; set; } = new List<SmartToyCapabilityDefinition>();

        /// <summary>Authored Dialog Tag catalog (Dialog From Tags selection domain).</summary>
        public List<DialogTagDefinition> DialogTags { get; set; } = new List<DialogTagDefinition>();

        /// <summary>Authored Dialog Snippet catalog (portable text content, NOT Resources).</summary>
        public List<DialogSnippetDefinition> DialogSnippets { get; set; } = new List<DialogSnippetDefinition>();

        public List<SessionDefinition> Sessions { get; set; } = new List<SessionDefinition>();
        public List<PhaseDefinition> Phases { get; set; } = new List<PhaseDefinition>();
        public List<CardDefinition> Cards { get; set; } = new List<CardDefinition>();
        public List<ResourceDefinition> Resources { get; set; } = new List<ResourceDefinition>();
    }
}
