using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Capsule.Build.Configuration;
using Capsule.Build.Registry;

namespace Capsule.Build.Textures;

/// <summary>Writes a texture's member: a <c>TextureHandle</c> whose summary names every non-default setting and the file that set it.</summary>
internal static class TextureMembers
{
    internal static void Write(StringBuilder code, string indent, string identifier, Source texture, ResolvedTexture settings)
    {
        string? described = Describe(settings);
        code.Append(indent).Append("/// <summary><c>").Append(texture.Key).Append(texture.Extension).Append("</c>")
            .Append(described is null ? string.Empty : ", " + described).AppendLine(".</summary>");
        code.Append(indent).Append('[').Append(GeneratedAttributes.AssetName).Append('(')
            .Append(Literal.Of(texture.Key + texture.Extension)).AppendLine(")]");
        code.Append(indent).Append("public static ").Append(GeneratedTypes.TextureHandle).Append(' ').Append(identifier)
            .Append(" => new ").Append(GeneratedTypes.TextureHandle).Append('(').Append(Literal.Of(texture.Key)).Append(", ")
            .Append(Literal.Of(texture.Extension)).AppendLine(");");
    }

    // The clause the summary ends in, or null when every setting is its default, as: with atlas "game"
    // from <c>.config.json</c> and format "r8" from <c>glow.png.config.json</c>.
    private static string? Describe(ResolvedTexture texture)
    {
        List<string> parts = [];
        Describe(parts, "atlas", texture.Atlas is { } atlas ? $"\"{atlas}\"" : null, texture.AtlasSetBy);
        Describe(parts, "format", texture.Format == default ? null : Spell(texture.Format, AssetConfigJsonContext.Default.TextureFormatSetting), texture.FormatSetBy);
        Describe(parts, "sampling", texture.Sampling == default ? null : Spell(texture.Sampling, AssetConfigJsonContext.Default.TextureSamplingSetting), texture.SamplingSetBy);

        return parts.Count switch
        {
            0 => null,
            1 => "with " + parts[0],
            _ => $"with {string.Join(", ", parts[..^1])} and {parts[^1]}",
        };
    }

    // One part per non-default setting: its name, its spelled value and the file that set it.
    private static void Describe(List<string> parts, string setting, string? spelled, string? setBy)
    {
        if (spelled is not null)
        {
            parts.Add($"{setting} {spelled} from <c>{SecurityElement.Escape(setBy)}</c>");
        }
    }

    // A value as a config file spells it, quoted.
    private static string Spell<T>(T value, JsonTypeInfo<T> type) => JsonSerializer.Serialize(value, type);
}
