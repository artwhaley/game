namespace TruthCardGame.Content
{
    public abstract class GameActionDefinition
    {
        /// <summary>Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.</summary>
        public string Id { get; set; } = "";

        /// <summary>Display name (authoring convenience; nullable in the schema).</summary>
        public string Name { get; set; }

        public bool IsBlocking { get; set; } = true;
    }
}
