using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Build.Configuration;

/// <summary>A texture's settings, as a sidecar or a folder file's <c>"texture"</c> block holds them.</summary>
internal sealed class TextureConfigJson
{
    // A missing setting is null and inherits. Resolution fills every setting.

    /// <summary>Every texture setting and its values, as a refusal lists them.</summary>
    internal const string Menu =
        "A texture's settings are \"atlas\" (an atlas name or false), \"format\" (\"rgba\" or \"r8\") and \"sampling\" (\"scene\", \"point\" or \"linear\").";

    /// <summary>What a texture's sidecar holds, as a refusal describes it.</summary>
    internal const string Shape = "A sidecar holds its asset's settings, as { \"format\": \"r8\" }. " + Menu;

    public AtlasSetting? Atlas { get; set; }

    public TextureFormatSetting? Format { get; set; }

    public TextureSamplingSetting? Sampling { get; set; }
}

/// <summary>The atlas a texture packs onto, spelled as its name or <c>false</c>.</summary>
/// <param name="Name">The normalized atlas name, or null when the texture ships on its own.</param>
[JsonConverter(typeof(AtlasSettingConverter))]
internal readonly record struct AtlasSetting(string? Name);

internal sealed class AtlasSettingConverter : JsonConverter<AtlasSetting>
{
    public override AtlasSetting Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.False)
        {
            return new AtlasSetting(null);
        }

        // An atlas name is one key segment.
        return reader.TokenType == JsonTokenType.String && reader.GetString() is { } name && !name.Contains('/', StringComparison.Ordinal)
            && AssetPaths.NormalizeKey(name, out _) is { } atlas && AssetPaths.IsKey(atlas)
                ? new AtlasSetting(atlas)
                : throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, AtlasSetting value, JsonSerializerOptions options)
    {
        if (value.Name is null)
        {
            writer.WriteBooleanValue(false);
        }
        else
        {
            writer.WriteStringValue(value.Name);
        }
    }
}
