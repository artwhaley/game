using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TruthCardGame.Content.Json
{
    /// <summary>
    /// Loads/saves portable content documents (schemaVersion 1). Camel-case
    /// properties, explicit action discriminators, loud failures on unknown
    /// action types and unsupported schema versions. Desktop-only; Game.Content
    /// itself stays free of any JSON attributes.
    /// </summary>
    public static class ContentJson
    {
        public const int CurrentSchemaVersion = 1;

        public static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            };
            options.Converters.Add(new ActionDefinitionConverter());
            return options;
        }

        public static ContentDocument Load(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number)
            {
                throw new JsonException("Content document is missing numeric 'schemaVersion'.");
            }

            var version = versionElement.GetInt32();
            if (version != CurrentSchemaVersion)
            {
                throw new JsonException(
                    $"Unsupported schemaVersion {version}; this serializer understands only {CurrentSchemaVersion}.");
            }

            return JsonSerializer.Deserialize<ContentDocument>(json, Options)
                ?? throw new JsonException("Content document deserialized to null.");
        }

        public static string Save(ContentDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.SchemaVersion = CurrentSchemaVersion;
            return JsonSerializer.Serialize(document, Options);
        }
    }
}
