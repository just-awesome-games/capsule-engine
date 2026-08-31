using System.Text.Json.Serialization;

namespace Capsule.Scenes.Documents;

// Reflection-based serialization is off solution-wide, so this generated context is the only way a
// scene document is read or written. TileGridJson is serializable in its own right because the
// tile-map entry's properties are read and written as a nested document of their own.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SceneDocumentJson))]
[JsonSerializable(typeof(TileGridJson))]
internal sealed partial class SceneDocumentJsonContext : JsonSerializerContext;
