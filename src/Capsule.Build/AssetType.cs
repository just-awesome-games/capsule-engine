using Capsule.Build.Audio;

namespace Capsule.Build;

/// <summary>One type of authored source, admitted by its extension wherever it sits under <c>Assets/</c>.</summary>
/// <param name="suffix">The word its members end in, as <c>player.png</c> is <c>PlayerTexture</c>.</param>
/// <param name="extensions">The extensions it admits, matched without regard to case.</param>
internal sealed class AssetType(string suffix, params string[] extensions)
{
    internal static readonly AssetType Textures = new("Texture", ".png");

    internal static readonly AssetType Audio = new("Sound", OggProbe.Extension, WavProbe.Extension);

    internal static readonly AssetType Fonts = new("Font", ".fnt");

    internal static readonly AssetType Shaders = new("Shader", ".fx");

    internal static readonly AssetType Scenes = new("Scene", ".scene.json");

    internal static readonly AssetType Sheets = new("Sheet", ".sheet.json");

    internal static readonly AssetType Clips = new("Clip", ".clip.json");

    /// <summary>A <c>&lt;tag&gt;.strings.json</c>, one language's texts, which <c>CapsuleStrings</c> declares and never <c>CapsuleAssets</c>.</summary>
    internal static readonly AssetType Strings = new("Strings", Build.Strings.StringFile.Extension);

    /// <summary>A <c>&lt;name&gt;.atlas.json</c>, which declares an atlas and is never an asset itself.</summary>
    internal static readonly AssetType Atlases = new("Atlas", ".atlas.json");

    /// <summary>
    /// A <c>*.config.json</c>, which configures other assets and is never an asset itself.
    /// </summary>
    /// <remarks>
    /// A <c>.config.json</c> keys as its folder's key and a trailing '/'. A sidecar keys as the key
    /// and lower-case extension of the file it names, as <c>textures/glow.png</c>.
    /// </remarks>
    internal static readonly AssetType Configs = new("Config", ".config.json");

    private static readonly AssetType[] Admitting = [Textures, Audio, Fonts, Shaders, Scenes, Sheets, Clips, Strings, Atlases, Configs];

    internal string Suffix { get; } = suffix;

    internal string[] Extensions { get; } = extensions;

    /// <summary>Every extension a type admits, which no importer may claim.</summary>
    internal static IEnumerable<string> AdmittedExtensions => Admitting.SelectMany(static type => type.Extensions);

    /// <summary>The type admitting <paramref name="name"/> by its extension, or null for a file none reads.</summary>
    internal static AssetType? Of(string name) => Array.Find(Admitting, type => type.Extension(name) is not null);

    /// <summary>The admitted extension <paramref name="name"/> ends in, as it spelled it, or null.</summary>
    internal string? Extension(string name)
    {
        foreach (string extension in Extensions)
        {
            if ((name.Length > extension.Length || this == Configs) && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return name[^extension.Length..];
            }
        }

        return null;
    }
}
