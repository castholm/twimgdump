using System.Text.Json;

namespace TwimgDump.Json
{
    internal static class JsonNodeExtensions
    {
        public static JsonNode AsJsonNode(this JsonElement element)
            => new JsonNode(element);
    }
}
