using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TruthCardGame.Content
{
    /// <summary>
    /// Dependency-free, deterministic JSON for the presentation-only
    /// PresentationCatalog artifact. Kept deliberately small and explicit: the
    /// portable assemblies carry no third-party JSON dependency, and both Unity
    /// and the .NET/WPF hosts must read the exact same bytes. It writes fields in
    /// a fixed order with invariant formatting so re-generation is stable, and
    /// parses with loud, positioned errors rather than silent recovery.
    /// </summary>
    public static class PresentationCatalogJson
    {
        public static string ToJson(PresentationCatalogDefinition catalog, bool indented = true)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var writer = new JsonWriter(indented);
            WriteCatalog(writer, catalog);
            return writer.ToString();
        }

        public static PresentationCatalogDefinition FromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var value = JsonParser.Parse(json);
            if (!(value is Dictionary<string, object> root))
            {
                throw new FormatException("PresentationCatalog JSON root must be an object.");
            }

            var catalog = new PresentationCatalogDefinition
            {
                CatalogVersion = Int(root, "catalogVersion"),
                GeneratedAtUtc = String(root, "generatedAtUtc"),
            };

            foreach (var item in ObjectArray(root, "ingredients"))
            {
                var ingredient = new PresentationIngredientDefinition
                {
                    Id = String(item, "id"),
                    DisplayName = String(item, "displayName"),
                    Kind = String(item, "kind"),
                    Enabled = Bool(item, "enabled"),
                    OwnsHead = Bool(item, "ownsHead"),
                };
                AddStrings(item, "performanceTagIds", ingredient.PerformanceTagIds);
                AddStrings(item, "supportedPostureIds", ingredient.SupportedPostureIds);
                AddStrings(item, "supportedAnchorIds", ingredient.SupportedAnchorIds);
                catalog.Ingredients.Add(ingredient);
            }

            foreach (var item in ObjectArray(root, "anchors"))
            {
                var anchor = new PresentationAnchorDefinition
                {
                    Id = String(item, "id"),
                    DisplayName = String(item, "displayName"),
                    LocationGroup = String(item, "locationGroup"),
                };
                AddStrings(item, "supportedPostureIds", anchor.SupportedPostureIds);
                AddStrings(item, "connectedAnchorIds", anchor.ConnectedAnchorIds);
                catalog.Anchors.Add(anchor);
            }

            foreach (var item in ObjectArray(root, "operations"))
            {
                var operation = new PresentationOperationDefinition
                {
                    Id = String(item, "id"),
                    DisplayName = String(item, "displayName"),
                    Kind = String(item, "kind"),
                    Cost = Int(item, "cost"),
                };
                AddStrings(item, "applicableAnchorIds", operation.ApplicableAnchorIds);
                catalog.Operations.Add(operation);
            }

            return catalog;
        }

        // ---------- writing ----------

        private static void WriteCatalog(JsonWriter writer, PresentationCatalogDefinition catalog)
        {
            writer.BeginObject();
            writer.Property("catalogVersion", catalog.CatalogVersion);
            writer.Property("generatedAtUtc", catalog.GeneratedAtUtc ?? "");

            writer.BeginArrayProperty("ingredients");
            foreach (var ingredient in catalog.Ingredients ?? new List<PresentationIngredientDefinition>())
            {
                if (ingredient == null) continue;
                writer.BeginObject();
                writer.Property("id", ingredient.Id ?? "");
                writer.Property("displayName", ingredient.DisplayName ?? "");
                writer.Property("kind", ingredient.Kind ?? "");
                writer.Property("enabled", ingredient.Enabled);
                writer.StringArrayProperty("performanceTagIds", ingredient.PerformanceTagIds);
                writer.StringArrayProperty("supportedPostureIds", ingredient.SupportedPostureIds);
                writer.StringArrayProperty("supportedAnchorIds", ingredient.SupportedAnchorIds);
                writer.Property("ownsHead", ingredient.OwnsHead);
                writer.EndObject();
            }
            writer.EndArray();

            writer.BeginArrayProperty("anchors");
            foreach (var anchor in catalog.Anchors ?? new List<PresentationAnchorDefinition>())
            {
                if (anchor == null) continue;
                writer.BeginObject();
                writer.Property("id", anchor.Id ?? "");
                writer.Property("displayName", anchor.DisplayName ?? "");
                writer.Property("locationGroup", anchor.LocationGroup ?? "");
                writer.StringArrayProperty("supportedPostureIds", anchor.SupportedPostureIds);
                writer.StringArrayProperty("connectedAnchorIds", anchor.ConnectedAnchorIds);
                writer.EndObject();
            }
            writer.EndArray();

            writer.BeginArrayProperty("operations");
            foreach (var operation in catalog.Operations ?? new List<PresentationOperationDefinition>())
            {
                if (operation == null) continue;
                writer.BeginObject();
                writer.Property("id", operation.Id ?? "");
                writer.Property("displayName", operation.DisplayName ?? "");
                writer.Property("kind", operation.Kind ?? "");
                writer.Property("cost", operation.Cost);
                writer.StringArrayProperty("applicableAnchorIds", operation.ApplicableAnchorIds);
                writer.EndObject();
            }
            writer.EndArray();

            writer.EndObject();
        }

        // ---------- reading helpers ----------

        private static void AddStrings(Dictionary<string, object> source, string key, List<string> target)
        {
            if (!source.TryGetValue(key, out var value) || value == null) return;
            if (!(value is List<object> list))
            {
                throw new FormatException($"PresentationCatalog field '{key}' must be an array.");
            }
            foreach (var item in list)
            {
                target.Add(item as string ?? throw new FormatException(
                    $"PresentationCatalog field '{key}' must contain only strings."));
            }
        }

        private static IEnumerable<Dictionary<string, object>> ObjectArray(Dictionary<string, object> source, string key)
        {
            if (!source.TryGetValue(key, out var value) || value == null) yield break;
            if (!(value is List<object> list))
            {
                throw new FormatException($"PresentationCatalog field '{key}' must be an array.");
            }
            foreach (var item in list)
            {
                if (!(item is Dictionary<string, object> entry))
                {
                    throw new FormatException($"PresentationCatalog field '{key}' must contain only objects.");
                }
                yield return entry;
            }
        }

        private static string String(Dictionary<string, object> source, string key)
        {
            if (!source.TryGetValue(key, out var value) || value == null) return "";
            if (value is string text) return text;
            throw new FormatException($"PresentationCatalog field '{key}' must be a string.");
        }

        private static int Int(Dictionary<string, object> source, string key)
        {
            if (!source.TryGetValue(key, out var value) || value == null) return 0;
            if (value is double number) return checked((int)number);
            throw new FormatException($"PresentationCatalog field '{key}' must be a number.");
        }

        private static bool Bool(Dictionary<string, object> source, string key)
        {
            if (!source.TryGetValue(key, out var value) || value == null) return false;
            if (value is bool flag) return flag;
            throw new FormatException($"PresentationCatalog field '{key}' must be a boolean.");
        }

        // ---------- writer ----------

        private sealed class JsonWriter
        {
            private readonly StringBuilder _builder = new StringBuilder();
            private readonly bool _indented;
            private int _depth;
            private bool _firstInContainer = true;

            public JsonWriter(bool indented)
            {
                _indented = indented;
            }

            public override string ToString() => _builder.ToString();

            public void BeginObject()
            {
                WriteValuePrefix();
                _builder.Append('{');
                BeginContainer();
            }

            public void EndObject()
            {
                _depth--;
                NewLineIndent();
                _builder.Append('}');
                EndContainer();
            }

            public void BeginArrayProperty(string name)
            {
                WritePropertyPrefix(name);
                _builder.Append('[');
                BeginContainer();
            }

            public void EndArray()
            {
                _depth--;
                NewLineIndent();
                _builder.Append(']');
                EndContainer();
            }

            public void Property(string name, string value)
            {
                WritePropertyPrefix(name);
                AppendString(value ?? "");
                _firstInContainer = false;
            }

            public void Property(string name, int value)
            {
                WritePropertyPrefix(name);
                _builder.Append(value.ToString(CultureInfo.InvariantCulture));
                _firstInContainer = false;
            }

            public void Property(string name, bool value)
            {
                WritePropertyPrefix(name);
                _builder.Append(value ? "true" : "false");
                _firstInContainer = false;
            }

            public void StringArrayProperty(string name, List<string> values)
            {
                WritePropertyPrefix(name);
                _builder.Append('[');
                var list = values ?? new List<string>();
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) _builder.Append(_indented ? ", " : ",");
                    AppendString(list[i] ?? "");
                }
                _builder.Append(']');
                _firstInContainer = false;
            }

            private void WritePropertyPrefix(string name)
            {
                if (!_firstInContainer) _builder.Append(',');
                NewLineIndent(depthOverride: _depth);
                AppendString(name ?? "");
                _builder.Append(':');
                if (_indented) _builder.Append(' ');
            }

            private void WriteValuePrefix()
            {
                // Object/array as a value (ingredient/operation entries): separators handled above.
                if (!_firstInContainer) _builder.Append(',');
                NewLineIndent(depthOverride: _depth);
            }

            private void BeginContainer()
            {
                _depth++;
                _firstInContainer = true;
            }

            private void EndContainer()
            {
                _firstInContainer = false;
            }

            private void NewLineIndent(int? depthOverride = null)
            {
                if (!_indented || _builder.Length == 0) return;
                _builder.Append('\n');
                var depth = depthOverride ?? _depth;
                for (var i = 0; i < depth; i++) _builder.Append("  ");
            }

            private void AppendString(string value)
            {
                _builder.Append('"');
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '"': _builder.Append("\\\""); break;
                        case '\\': _builder.Append("\\\\"); break;
                        case '\b': _builder.Append("\\b"); break;
                        case '\f': _builder.Append("\\f"); break;
                        case '\n': _builder.Append("\\n"); break;
                        case '\r': _builder.Append("\\r"); break;
                        case '\t': _builder.Append("\\t"); break;
                        default:
                            if (c < ' ')
                            {
                                _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                _builder.Append(c);
                            }
                            break;
                    }
                }
                _builder.Append('"');
            }
        }

        // ---------- parser ----------

        private sealed class JsonParser
        {
            private readonly string _text;
            private int _index;

            private JsonParser(string text)
            {
                _text = text;
            }

            public static object Parse(string text)
            {
                var parser = new JsonParser(text);
                parser.SkipWhitespace();
                var value = parser.ParseValue();
                parser.SkipWhitespace();
                if (parser._index != parser._text.Length)
                {
                    throw parser.Error("unexpected trailing content");
                }
                return value;
            }

            private object ParseValue()
            {
                if (_index >= _text.Length) throw Error("unexpected end of input");
                var c = _text[_index];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _index++; // {
                SkipWhitespace();
                if (TryConsume('}')) return result;
                while (true)
                {
                    SkipWhitespace();
                    if (_index >= _text.Length || _text[_index] != '"') throw Error("expected object property name");
                    var name = ParseString();
                    SkipWhitespace();
                    if (!TryConsume(':')) throw Error("expected ':' after property name");
                    SkipWhitespace();
                    result[name] = ParseValue();
                    SkipWhitespace();
                    if (TryConsume(',')) continue;
                    if (TryConsume('}')) return result;
                    throw Error("expected ',' or '}' in object");
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                _index++; // [
                SkipWhitespace();
                if (TryConsume(']')) return result;
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(',')) continue;
                    if (TryConsume(']')) return result;
                    throw Error("expected ',' or ']' in array");
                }
            }

            private string ParseString()
            {
                _index++; // opening quote
                var builder = new StringBuilder();
                while (true)
                {
                    if (_index >= _text.Length) throw Error("unterminated string");
                    var c = _text[_index++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (_index >= _text.Length) throw Error("unterminated escape");
                    var escape = _text[_index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length) throw Error("truncated \\u escape");
                            var hex = _text.Substring(_index, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                throw Error("invalid \\u escape");
                            }
                            builder.Append((char)code);
                            _index += 4;
                            break;
                        default: throw Error($"invalid escape '\\{escape}'");
                    }
                }
            }

            private double ParseNumber()
            {
                var start = _index;
                while (_index < _text.Length)
                {
                    var c = _text[_index];
                    if (c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E' || (c >= '0' && c <= '9'))
                    {
                        _index++;
                    }
                    else
                    {
                        break;
                    }
                }
                var token = _text.Substring(start, _index - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    throw Error($"invalid number '{token}'");
                }
                return value;
            }

            private void Expect(string literal)
            {
                if (_index + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                {
                    throw Error($"expected '{literal}'");
                }
                _index += literal.Length;
            }

            private bool TryConsume(char c)
            {
                if (_index < _text.Length && _text[_index] == c)
                {
                    _index++;
                    return true;
                }
                return false;
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length)
                {
                    var c = _text[_index];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _index++;
                    else break;
                }
            }

            private FormatException Error(string message)
            {
                return new FormatException($"PresentationCatalog JSON at index {_index}: {message}.");
            }
        }
    }
}
