namespace TruthCardGame.Content
{
    /// <summary>
    /// Authored definition of one Session-global Temperature runtime variable.
    /// Mutations clamp to [MinValue, MaxValue]; unset Session values start at
    /// DefaultValue; spawn overrides replace the default per-value (missing keys
    /// keep the definition default). Happiness ships seeded as 0/100/default 50.
    ///
    /// Core uses a dictionary keyed by stable Temperature ID — never a hardcoded
    /// Happiness field — so new temperatures require only content, not engine code.
    /// </summary>
    public sealed class TemperatureDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";

        public float MinValue { get; set; }
        public float MaxValue { get; set; } = 100f;
        public float DefaultValue { get; set; } = 50f;
    }
}
