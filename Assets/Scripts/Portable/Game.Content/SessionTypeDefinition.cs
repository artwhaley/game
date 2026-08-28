using System.Collections.Generic;

namespace TruthCardGame.Content
{
    /// <summary>
    /// A simple authored category for Sessions (e.g. JOI, Stroker Toy, Butt Stuff).
    /// A Session references exactly one SessionType. The consumer flow picks a
    /// type, then a Session of that type is selected uniformly among eligible
    /// ones. May require zero or more Smart Toy capabilities; all listed are
    /// required for eligibility.
    /// </summary>
    public sealed class SessionTypeDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int SortOrder { get; set; }

        /// <summary>Every listed capability is required for eligibility.</summary>
        public List<string> RequiredCapabilityIds { get; set; } = new List<string>();
    }
}
