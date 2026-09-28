using Capsule.Assets;

namespace Capsule.Build.Textures;

/// <summary>One texture's settings after resolution, each with the file under <c>Assets/</c> that set it.</summary>
/// <remarks>A setting no file set holds the engine default, and its file is null.</remarks>
internal sealed class ResolvedTexture
{
    /// <summary>The atlas the texture packs onto, or null when it ships on its own.</summary>
    internal string? Atlas { get; set; }

    internal TextureFormatSetting Format { get; set; } = TextureFormatSetting.Rgba;

    internal TextureSamplingSetting Sampling { get; set; } = TextureSamplingSetting.Scene;

    internal string? AtlasSetBy { get; set; }

    internal string? FormatSetBy { get; set; }

    internal string? SamplingSetBy { get; set; }
}
