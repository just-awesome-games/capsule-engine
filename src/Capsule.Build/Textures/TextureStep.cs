using Capsule.Assets;
using Capsule.Build.Caching;
using Capsule.Build.Registry;

namespace Capsule.Build.Textures;

/// <summary>
/// Ships every texture no atlas packs at its path, adds its non-default facts to the texture map, and
/// declares every texture as a <c>TextureHandle</c>.
/// </summary>
internal static class TextureStep
{
    internal static void Run(PipelinePass pass)
    {
        List<Source> packed = [];
        List<Source> unpacked = [];
        foreach (Source texture in pass.Of(AssetType.Textures))
        {
            (pass.TextureSettings[texture.Key].Atlas is null ? unpacked : packed).Add(texture);
        }

        // An unpacked texture ships as authored, or an r8 texture as an 8-bit greyscale PNG of its values.
        List<Source> shipped = pass.Each(
            "textures",
            unpacked,
            source => Derivation.Of(source, pass.TextureSettings[source.Key].Format == TextureFormatSetting.R8 ? "format=r8" : string.Empty),
            (source, files) =>
            {
                string path = source.Key + source.Extension;
                if (pass.TextureSettings[source.Key].Format == TextureFormatSetting.R8)
                {
                    Texels values = TexturePixels.Decode(source.Path, TextureFormatSetting.R8);
                    files.Write(path, temporary =>
                    {
                        using FileStream file = File.Create(temporary);
                        PngWriter.Write(values.Data, values.Width, values.Height, 1, file);
                    });
                }
                else
                {
                    files.Copy(source.Path, path);
                }
            });

        foreach (Source texture in shipped)
        {
            ResolvedTexture settings = pass.TextureSettings[texture.Key];
            if (TextureEntryJson.Facts(settings.Format, settings.Sampling) is { } facts)
            {
                pass.TextureMap.AddTexture(texture.Key, facts);
            }
        }

        foreach (Source texture in packed.Concat(shipped))
        {
            pass.Assets.Beside(GeneratedAttributes.Asset);
            pass.Declare(texture, pass.TextureSettings[texture.Key], TextureMembers.Write);
        }
    }
}
