using System.Text.Json.Serialization;

namespace Capsule.Maps;

// Reflection-based serialization is off solution-wide, so this generated context is the only
// way a map is read or written.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MapJson))]
internal sealed partial class MapJsonContext : JsonSerializerContext;
