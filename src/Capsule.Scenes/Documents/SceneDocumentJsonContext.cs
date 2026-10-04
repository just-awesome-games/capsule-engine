using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Scenes.Documents;

// Reflection-based serialization is off solution-wide, so this generated context is the only way to read or
// write a scene document. A field the format does not declare fails the document, except where a class takes
// its undeclared keys as authorable members. The dictionary turns those keys into the element an applier reads.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SceneDocumentJson))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class SceneDocumentJsonContext : JsonSerializerContext;
