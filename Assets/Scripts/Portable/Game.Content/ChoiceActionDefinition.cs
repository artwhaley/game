using System.Collections.Generic;

namespace TruthCardGame.Content
{
    public sealed class ChoiceActionDefinition : GameActionDefinition
    {
        public string Prompt { get; set; } = "";
        public List<ChoiceOptionDefinition> Options { get; set; } = new List<ChoiceOptionDefinition>();
    }
}
