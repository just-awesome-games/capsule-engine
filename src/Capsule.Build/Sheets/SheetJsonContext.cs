using System.Text.Json.Serialization;

namespace Capsule.Build.Sheets;

// Reflection-based serialization is off solution-wide. A member the format does not declare fails the sheet.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SheetJson))]
internal sealed partial class SheetJsonContext : JsonSerializerContext;
