using System.Text.Json.Serialization;

namespace Capsule.Scenes.Documents;

// Reflection-based serialization is off solution-wide, so this generated context is the only way to read or
// write a scene document. TileGridJson is serializable on its own because a tile-map entry's properties form a
// nested document.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SceneDocumentJson))]
[JsonSerializable(typeof(TileGridJson))]
internal sealed partial class SceneDocumentJsonContext : JsonSerializerContext;
