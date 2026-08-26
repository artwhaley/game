namespace TruthCardGame.Content
{
    /// <summary>
    /// A candidate occurrence inside a PhaseSlot: one stable ID referencing a
    /// reusable Phase by ID. Future conditions/weights attach to this
    /// occurrence, not to the referenced Phase — hence the candidate's own ID.
    /// </summary>
    public sealed class PhaseSlotCandidateDefinition
    {
        public string Id { get; set; } = "";
        public string PhaseId { get; set; } = "";
    }
}
