using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// An indexed, ordered list of Action Instances. Sequences are owned exactly
    /// once (by a Card, a Phase action node / decision option, a SessionDecision
    /// option, or a PromptChoice option); instances never appear in two sequences.
    /// List order is the execution order and the persistence ordinal source of truth.
    ///
    /// A flow transfer inside the sequence does not discard later Actions: they are
    /// resumed from the saved continuation if control eventually returns.
    /// </summary>
    public sealed class ActionSequenceDefinition
    {
        public string Id { get; set; } = "";
        public List<ActionInstanceDefinition> Instances { get; set; } = new List<ActionInstanceDefinition>();
    }
}
