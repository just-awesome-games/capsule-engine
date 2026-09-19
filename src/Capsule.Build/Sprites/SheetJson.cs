using System.Text.Json.Serialization;

namespace Capsule.Build.Sprites;

// The document as the JSON spells it. Every member is optional here. A missing one is then refused
// by name instead of as a parse failure, and a member the format does not declare fails the sheet.
internal sealed class SheetJson
{
    public int? FormatVersion { get; set; }

    public string? Texture { get; set; }

    public List<SocketJson>? Sockets { get; set; }

    public List<FrameJson>? Frames { get; set; }

    public List<ClipJson>? Clips { get; set; }

    /// <summary>What a derived sheet came from. Accepted, and read by nothing.</summary>
    public SourceJson? Source { get; set; }
}

internal sealed class SocketJson
{
    public string? Name { get; set; }
}

internal sealed class FrameJson
{
    public string? Name { get; set; }

    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public float[]? Pivot { get; set; }

    public Dictionary<string, float[]>? Sockets { get; set; }
}

internal sealed class ClipJson
{
    public string? Name { get; set; }

    public bool? Loop { get; set; }

    public List<ClipFrameJson>? Frames { get; set; }
}

internal sealed class ClipFrameJson
{
    public string? Frame { get; set; }

    public int? Ticks { get; set; }
}

internal sealed class SourceJson
{
    public string? Tool { get; set; }

    public string? Path { get; set; }

    public string? Hash { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SheetJson))]
internal sealed partial class SheetJsonContext : JsonSerializerContext;
