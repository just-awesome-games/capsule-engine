using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Scenes;

// Reflection-based serialization is off solution-wide. This generated context is the only way to read or
// write a scene document. The document and each entry skip undeclared keys here, and the generated applier
// refuses any that no authorable member takes. The writer writes those members from a dictionary.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SceneDocumentJson))]
internal sealed partial class SceneDocumentJsonContext : JsonSerializerContext;
