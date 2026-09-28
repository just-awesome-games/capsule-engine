using System.Text.Json.Serialization;

namespace Capsule.Scenes.Documents;

// Reflection-based serialization is off solution-wide, so this generated context is the only way to read or
// write a scene document. A field the format does not declare fails the document, so a typo in a hand-authored
// one fails at load instead of in play. TileGridJson is serializable on its own because a tile-map entry's
// properties form a nested document.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SceneDocumentJson))]
[JsonSerializable(typeof(TileGridJson))]
internal sealed partial class SceneDocumentJsonContext : JsonSerializerContext;
