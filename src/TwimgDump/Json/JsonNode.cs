using System.Collections.Generic;
using System.Text.Json;

namespace TwimgDump.Json
{
    /// <summary>
    /// Provides a convenient and forgiving API for navigating a <see cref="JsonElement"/>.
    /// </summary>
    internal readonly struct JsonNode
    {
        private readonly JsonElement _element;

        public JsonNode(JsonElement element)
            => _element = element;

        public JsonNodeType Type
            => _element.ValueKind switch
            {
                JsonValueKind.Undefined => JsonNodeType.None,
                JsonValueKind.Object => JsonNodeType.Object,
                JsonValueKind.Array => JsonNodeType.Array,
                JsonValueKind.String => JsonNodeType.String,
                JsonValueKind.Number => JsonNodeType.Number,
                JsonValueKind.True => JsonNodeType.Boolean,
                JsonValueKind.False => JsonNodeType.Boolean,
                JsonValueKind.Null => JsonNodeType.Null,
                _ => JsonNodeType.None,
            };

        public int? Length
            => _element.ValueKind == JsonValueKind.Array
                ? (int?)_element.GetArrayLength()
                : null;

        public IEnumerable<JsonNode> Elements
        {
            get
            {
                if (_element.ValueKind != JsonValueKind.Array)
                {
                    yield break;
                }

                foreach (var element in _element.EnumerateArray())
                {
                    yield return new JsonNode(element);
                }
            }
        }

        public IEnumerable<(string Name, JsonNode Value)> Members
        {
            get
            {
                if (_element.ValueKind != JsonValueKind.Object)
                {
                    yield break;
                }

                foreach (var element in _element.EnumerateObject())
                {
                    yield return (element.Name, new JsonNode(element.Value));
                }
            }
        }

        public JsonNode this[int? index]
            => index is int
            && _element.ValueKind == JsonValueKind.Array
            && (uint)index < (uint)_element.GetArrayLength()
                ? new JsonNode(_element[index.Value])
                : default;

        public JsonNode this[string? name]
            => name is object
            && _element.ValueKind == JsonValueKind.Object
            && _element.TryGetProperty(name, out var element)
                ? new JsonNode(element)
                : default;

        public string? GetString()
            => _element.ValueKind == JsonValueKind.String
                ? _element.GetString()
                : null;

        public byte? GetByte()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetByte(out var value)
                ? (byte?)value
                : null;

        public short? GetInt16()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetInt16(out var value)
                ? (short?)value
                : null;

        public int? GetInt32()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetInt32(out var value)
                ? (int?)value
                : null;

        public long? GetInt64()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetInt64(out var value)
                ? (long?)value
                : null;

        public sbyte? GetSByte()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetSByte(out var value)
                ? (sbyte?)value
                : null;

        public ushort? GetUInt16()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetUInt16(out var value)
                ? (ushort?)value
                : null;

        public uint? GetUInt32()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetUInt32(out var value)
                ? (uint?)value
                : null;

        public ulong? GetUInt64()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetUInt64(out var value)
                ? (ulong?)value
                : null;

        public float? GetSingle()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetSingle(out var value)
                ? (float?)value
                : null;

        public double? GetDouble()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetDouble(out var value)
                ? (double?)value
                : null;

        public decimal? GetDecimal()
            => _element.ValueKind == JsonValueKind.Number
            && _element.TryGetDecimal(out var value)
                ? (decimal?)value
                : null;

        public bool? GetBoolean()
            => _element.ValueKind == JsonValueKind.True
            || _element.ValueKind == JsonValueKind.False
                ? (bool?)_element.GetBoolean()
                : null;

        public JsonElement AsJsonElement()
            => _element;

        public override string ToString()
            => _element.ToString();
    }
}
