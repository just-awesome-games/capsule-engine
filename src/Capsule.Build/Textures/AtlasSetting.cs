using System.Text.Json.Serialization;

namespace Capsule.Build.Textures;

/// <summary>The atlas a texture packs onto, spelled as its name or <c>false</c>.</summary>
/// <param name="Name">The normalized atlas name, or null when the texture ships on its own.</param>
[JsonConverter(typeof(AtlasSettingConverter))]
internal readonly record struct AtlasSetting(string? Name);
