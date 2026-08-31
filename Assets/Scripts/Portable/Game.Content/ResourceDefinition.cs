namespace TruthCardGame.Content
{
    /// <summary>
    /// Portable resource entity referenced by stable ID (current kinds:
    /// 'cutscene' and 'toy_pattern'). Host bindings (unity_*, wpf_*) attach to resources; Core
    /// only carries the portable ID and kind.
    /// </summary>
    public sealed class ResourceDefinition
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
