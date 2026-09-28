using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Build.Textures;

/// <summary>Reads and writes an <see cref="AtlasSetting"/> as an atlas name or <c>false</c>.</summary>
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
