using System.Text.Json;
using Capsule.Assets;
using Capsule.Build.Atlases;
using Capsule.Build.Configuration;
using Capsule.Build.Registry;

namespace Capsule.Build.Textures;

/// <summary>The texture pass, which ships every unpacked texture at its path and declares every texture as a <c>TextureHandle</c>.</summary>
internal static class TextureStep
{
    private const string HandleType = "global::Capsule.Assets.TextureHandle";

    /// <summary>The bytes a texel of <paramref name="format"/> ships in.</summary>
    internal static int Channels(TextureFormatSetting format) => format == TextureFormatSetting.R8 ? 1 : 4;

    /// <param name="settings">Every texture's resolved settings.</param>
    /// <param name="setBy">The file that set each value, which a member's summary names.</param>
    /// <param name="map">The atlas pass's map, holding every packed texture. Unpacked facts are added to it.</param>
    internal static void Build(
        BuildPass pass,
        Dictionary<string, TextureConfigJson> settings,
        Dictionary<(string Texture, string Setting), string> setBy,
        TextureMapJson map)
    {
        IDictionary<string, TextureEntryJson> entries = map.Textures!;
        foreach ((Source texture, _) in pass.Each(
            pass.Of(AssetType.Textures),
            source =>
            {
                TextureConfigJson resolved = settings[source.Key];
                if (resolved.Atlas!.Value.Name is null)
                {
                    // An r8 texture ships as an 8-bit greyscale PNG of its values.
                    if (Channels(resolved.Format!.Value) == 1)
                    {
                        Texels values = SingleChannelPng.Read(File.ReadAllBytes(source.Path));
                        AtomicFile.Write(pass.Shipped.Claim(source.Key + source.Extension, $"'{source.Path}'"), path =>
                        {
                            using FileStream file = File.Create(path);
                            AtlasStep.Encode(values.Data, values.Width, values.Height, file, values.Channels);
                        });
                    }
                    else
                    {
                        pass.Shipped.Copy(source.Path, source.Key + source.Extension);
                    }

                    if (TextureEntryJson.Facts(resolved.Format.Value, resolved.Sampling!.Value) is { } facts)
                    {
                        entries.Add(source.Key, facts);
                    }
                }

                return source;
            }))
        {
            string? described = AssetConfig.Describe(texture.Key, settings[texture.Key], setBy);
            pass.Assets.Beside(CapsuleAssetsFile.AssetAttribute);
            pass.Declare(texture, (source, indent, identifier) =>
            {
                source.Append(indent).Append("/// <summary><c>").Append(texture.Key).Append(texture.Extension).Append("</c>")
                    .Append(described is null ? string.Empty : ", " + described).AppendLine(".</summary>");
                source.Append(indent).Append('[').Append(CapsuleAssetsFile.AssetAttributeName).Append('(')
                    .Append(Literal.Of(texture.Key + texture.Extension)).AppendLine(")]");
                source.Append(indent).Append("public static ").Append(HandleType).Append(' ').Append(identifier)
                    .Append(" => new ").Append(HandleType).Append('(').Append(Literal.Of(texture.Key)).Append(", ")
                    .Append(Literal.Of(texture.Extension)).AppendLine(");");
            });
        }

        // The map ships only when some texture has a non-default run-time fact.
        if (entries.Count > 0)
        {
            map.Pages = map.Pages is { Count: > 0 } ? map.Pages : null;
            AtomicFile.Write(pass.Shipped.Claim(TextureMapJson.ShippedPath, "the texture map"), path =>
            {
                using FileStream file = File.Create(path);
                JsonSerializer.Serialize(file, map, TextureMapJsonContext.Default.TextureMapJson);
            });
        }
    }
}
