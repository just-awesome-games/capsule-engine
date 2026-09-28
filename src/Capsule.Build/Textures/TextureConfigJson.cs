using System.ComponentModel;
using Capsule.Assets;

namespace Capsule.Build.Textures;

/// <summary>A texture's settings, as a sidecar or a folder file's <c>"texture"</c> block holds them.</summary>
internal class TextureConfigJson
{
    // A missing setting is null and inherits.

    /// <summary>Every texture setting and its values, as a refusal lists them.</summary>
    internal const string Menu =
        "A texture's settings are \"atlas\" (an atlas name or false), \"format\" (\"rgba\" or \"r8\") and \"sampling\" (\"scene\", \"point\" or \"linear\").";

    /// <summary>What a texture's sidecar holds, as a refusal describes it.</summary>
    internal const string Shape = "A sidecar holds its asset's settings, as { \"format\": \"r8\" }. " + Menu;

    [Description("The atlas this texture packs onto, or false to ship the texture on its own. A <name>.atlas.json under Assets/ declares the atlas <name>. A font's page never packs.")]
    public AtlasSetting? Atlas { get; set; }

    [Description("How the texture's texels are stored. \"rgba\" is four 8-bit channels. \"r8\" is one 8-bit channel, read from a non-interlaced greyscale PNG's values or an indexed PNG's palette indices.")]
    public TextureFormatSetting? Format { get; set; }

    [Description("How the texture is sampled. \"scene\" follows the scene's sampling. \"point\" and \"linear\" override it wherever the texture is drawn or sampled.")]
    public TextureSamplingSetting? Sampling { get; set; }
}
