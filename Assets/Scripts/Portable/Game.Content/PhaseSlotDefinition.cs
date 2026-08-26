using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// An ordered, session-owned composition seam. A slot never embeds a
    /// Phase; it owns ordered candidate occurrences that reference reusable
    /// Phases by ID. Exactly one candidate is valid for runtime in this
    /// milestone; zero is invalid and more than one is deliberately
    /// unimplemented (fail loudly, never pick silently, never consume RNG).
    /// </summary>
    public sealed class PhaseSlotDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public List<PhaseSlotCandidateDefinition> Candidates { get; set; } = new List<PhaseSlotCandidateDefinition>();
    }
}
