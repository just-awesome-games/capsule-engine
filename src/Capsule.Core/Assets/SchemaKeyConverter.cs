using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Assets;

// Reads a root "$schema", which names the file's JSON Schema for an editor. Its value is ignored, and
// anything but a string is refused. The same key anywhere below the root is an unknown field.
internal sealed class SchemaKeyConverter : JsonConverter<string>
{
    internal const string Key = "$schema";

    // A null reaches Read, which refuses it, instead of reading as an omitted key.
    public override bool HandleNull => true;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException(
                $"\"{Key}\" is {Kind(reader.TokenType)}. \"{Key}\" is the URL of this file's schema, and the build ignores it. Write the URL as a string, or remove \"{Key}\".");

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);

    private static string Kind(JsonTokenType token) => token switch
    {
        JsonTokenType.Null => "null",
        JsonTokenType.StartObject => "an object",
        JsonTokenType.StartArray => "an array",
        JsonTokenType.Number => "a number",
        _ => "a boolean",
    };
}
