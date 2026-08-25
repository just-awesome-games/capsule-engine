using System.Text.Json.Serialization;

namespace Capsule.Levels;

// Reflection-based serialization is off solution-wide, so this generated context is the only
// way a level is read or written.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LevelJson))]
internal sealed partial class LevelJsonContext : JsonSerializerContext;
