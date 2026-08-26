using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TruthCardGame.Content.Json
{
    /// <summary>
    /// Polymorphic converter for the GameActionDefinition hierarchy using an
    /// explicit, stable lower-case "type" discriminator. Unknown types and
    /// missing discriminators fail loudly — never silently coerced.
    /// </summary>
    public sealed class ActionDefinitionConverter : JsonConverter<GameActionDefinition>
    {
        private static string TokenFor(Type type)
        {
            if (type == typeof(DebugActionDefinition)) return "debug";
            if (type == typeof(StatIncreaseActionDefinition)) return "statIncrease";
            if (type == typeof(ChoiceActionDefinition)) return "choice";
            if (type == typeof(CutsceneActionDefinition)) return "cutscene";
            throw new JsonException($"No JSON type discriminator registered for '{type.Name}'.");
        }

        private static Type Resolve(string token)
        {
            switch (token)
            {
                case "debug": return typeof(DebugActionDefinition);
                case "statIncrease": return typeof(StatIncreaseActionDefinition);
                case "choice": return typeof(ChoiceActionDefinition);
                case "cutscene": return typeof(CutsceneActionDefinition);
                default:
                    throw new JsonException($"Unknown action type discriminator '{token}'.");
            }
        }

        public override GameActionDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var node = JsonNode.Parse(ref reader) as JsonObject
                ?? throw new JsonException("Action must be a JSON object.");

            var token = node["type"]?.GetValue<string>()
                ?? throw new JsonException("Action object is missing the required 'type' discriminator.");

            node.Remove("type");

            var concrete = (GameActionDefinition)node.Deserialize(Resolve(token), options);
            return concrete ?? throw new JsonException($"Action of type '{token}' deserialized to null.");
        }

        public override void Write(Utf8JsonWriter writer, GameActionDefinition value, JsonSerializerOptions options)
        {
            var body = JsonSerializer.SerializeToNode(value, value.GetType(), options) as JsonObject
                ?? throw new JsonException($"Action '{value.GetType().Name}' did not serialize to a JSON object.");

            var ordered = new JsonObject { ["type"] = TokenFor(value.GetType()) };
            foreach (var property in body)
            {
                ordered[property.Key] = property.Value?.DeepClone();
            }

            ordered.WriteTo(writer);
        }
    }
}
