using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Assets;

// The shipped texture map, holding only non-default run-time facts. The build writes it and the
// runtime reads it once at boot. A packed texture maps to its page and the texel its (0, 0) landed
// on. Any other texture and any page map to a non-default format or sampling. A game whose every
// texture is unpacked and default ships no map.
internal sealed class TextureMapJson
{
    // Where the map ships, below assets/. No key holds a '.', so no asset ships here.
    internal const string ShippedPath = "textures.json";

    [JsonPropertyName("pages")]
    public IDictionary<string, TextureEntryJson>? Pages { get; set; }

    [JsonPropertyName("textures")]
    public IDictionary<string, TextureEntryJson>? Textures { get; set; }
}

// One texture's or one page's facts. Each is null when it is the default.
internal sealed class TextureEntryJson
{
    [JsonPropertyName("page")]
    public string? Page { get; set; }

    [JsonPropertyName("x")]
    public int? X { get; set; }

    [JsonPropertyName("y")]
    public int? Y { get; set; }

    [JsonPropertyName("format")]
    public TextureFormatSetting? Format { get; set; }

    [JsonPropertyName("sampling")]
    public TextureSamplingSetting? Sampling { get; set; }

    // The entry for a file of format and sampling, or null when both are the default.
    internal static TextureEntryJson? Facts(TextureFormatSetting format, TextureSamplingSetting sampling) =>
        format == default && sampling == default
            ? null
            : new TextureEntryJson
            {
                Format = format == default ? null : format,
                Sampling = sampling == default ? null : sampling,
            };
}

// How a texture's texels are stored, as its config and the shipped map spell it.
[JsonConverter(typeof(LowerCaseEnumConverter<TextureFormatSetting>))]
internal enum TextureFormatSetting
{
    Rgba,

    // One 8-bit channel.
    R8,
}

// How a texture is sampled, as its config and the shipped map spell it.
[JsonConverter(typeof(LowerCaseEnumConverter<TextureSamplingSetting>))]
internal enum TextureSamplingSetting
{
    // Follows the scene's sampling.
    Scene,

    Point,

    Linear,
}

// An enum spelled as its member's name in lower case, read only in exactly that spelling.
internal sealed class LowerCaseEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && reader.GetString() is { } spelled
            && Enum.TryParse(spelled, ignoreCase: true, out T value) && spelled == Spell(value)
                ? value
                : throw new JsonException();

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(Spell(value));

    // A number parses to a member too, and then differs from the member's own spelling.
    private static string Spell(T value) => value.ToString().ToLowerInvariant();
}

// Reflection-based serialization is off solution-wide.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TextureMapJson))]
internal sealed partial class TextureMapJsonContext : JsonSerializerContext;
