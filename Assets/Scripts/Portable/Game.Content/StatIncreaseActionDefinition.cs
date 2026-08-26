namespace TruthCardGame.Content
{
    public sealed class StatIncreaseActionDefinition : GameActionDefinition
    {
        public string StatKey { get; set; } = "";
        public int Amount { get; set; } = 1;
    }
}
