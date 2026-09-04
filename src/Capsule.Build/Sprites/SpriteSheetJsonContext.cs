using System.Text.Json.Serialization;

namespace Capsule.Build.Sprites;

// Reflection-based serialization is off solution-wide, so this generated context is the only way a
// sheet document is read or written.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SpriteSheetJson))]
internal sealed partial class SpriteSheetJsonContext : JsonSerializerContext;
