using Capsule.Assets;
using Capsule.Build.Registry;

namespace Capsule.Build.Textures;

/// <summary>
/// Ships every texture no atlas packs at its path, adds its non-default facts to the texture map, and
/// declares every texture as a <c>TextureHandle</c>.
/// </summary>
internal static class TextureStep
{
    internal static void Run(BuildPass pass)
    {
        foreach ((Source texture, ResolvedTexture settings) in pass.Each(
            pass.Of(AssetType.Textures),
            source =>
            {
                ResolvedTexture settings = pass.TextureSettings[source.Key];
                if (settings.Atlas is null)
                {
                    // An r8 texture ships as an 8-bit greyscale PNG of its values.
                    if (TexturePixels.Channels(settings.Format) == 1)
                    {
                        Texels values = SingleChannelPng.Read(File.ReadAllBytes(source.Path));
                        AtomicFile.Write(pass.Shipped.Claim(source.Key + source.Extension, $"'{source.Path}'"), path =>
                        {
                            using FileStream file = File.Create(path);
                            TexturePixels.Encode(values.Data, values.Width, values.Height, file, values.Channels);
                        });
                    }
                    else
                    {
                        pass.Shipped.Copy(source.Path, source.Key + source.Extension);
                    }

                    if (TextureEntryJson.Facts(settings.Format, settings.Sampling) is { } facts)
                    {
                        pass.TextureMap.AddTexture(source.Key, facts);
                    }
                }

                pass.Progress("textures", source);

                return settings;
            }))
        {
            pass.Beside(GeneratedAttributes.Asset);
            pass.Declare(texture, settings, TextureMembers.Write);
        }
    }
}
