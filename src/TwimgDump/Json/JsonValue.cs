using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TwimgDump.Json
{
    /// <summary>
    /// Provides a convenient and forgiving API for navigating a <see cref="JsonDocument"/>.
    /// </summary>
    public readonly struct JsonValue
    {
        private readonly JsonElement _jsonElement;

        public JsonValue(JsonDocument document) => _jsonElement = document.RootElement;

        private JsonValue(JsonElement element) => _jsonElement = element;

        public JsonValueKind Kind => _jsonElement.ValueKind;

        public int? Length => _jsonElement.ValueKind == JsonValueKind.Array
            ? (int?)_jsonElement.GetArrayLength()
            : null;

        public IEnumerable<JsonValue> Elements => _jsonElement.ValueKind == JsonValueKind.Array
            ? _jsonElement.EnumerateArray().Select(x => new JsonValue(x))
            : Enumerable.Empty<JsonValue>();

        public IEnumerable<(string Name, JsonValue Value)> Members => _jsonElement.ValueKind == JsonValueKind.Object
            ? _jsonElement.EnumerateObject().Select(x => (x.Name, new JsonValue(x.Value)))
            : Enumerable.Empty<(string, JsonValue)>();

        public JsonValue this[int index] => _jsonElement.ValueKind == JsonValueKind.Array
            ? _jsonElement.EnumerateArray().Select(x => new JsonValue(x)).ElementAtOrDefault(index)
            : default;

        public JsonValue this[string? name] => _jsonElement.ValueKind == JsonValueKind.Object
            && name is object
            && _jsonElement.TryGetProperty(name, out var element)
            ? new JsonValue(element)
            : default;

        public bool? GetBoolean() => _jsonElement.ValueKind == JsonValueKind.True
            || _jsonElement.ValueKind == JsonValueKind.False
            ? (bool?)_jsonElement.GetBoolean()
            : null;

        public int? GetInt32() => _jsonElement.ValueKind == JsonValueKind.Number
            ? (int?)_jsonElement.GetInt32()
            : null;

        public long? GetInt64() => _jsonElement.ValueKind == JsonValueKind.Number
            ? (long?)_jsonElement.GetInt64()
            : null;

        public float? GetSingle() => _jsonElement.ValueKind == JsonValueKind.Number
            ? (float?)_jsonElement.GetSingle()
            : null;

        public double? GetDouble() => _jsonElement.ValueKind == JsonValueKind.Number
            ? (double?)_jsonElement.GetDouble()
            : null;

        public string? GetString() => _jsonElement.ValueKind == JsonValueKind.String
            ? _jsonElement.GetString()
            : null;
    }
}
