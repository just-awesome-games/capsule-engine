using System.Text.Json.Serialization;

namespace Capsule.Build.Sprites;

// The file shape, one-to-one with the JSON. JsonPropertyOrder fixes field order, which the
// canonical writer depends on: a reordered member here changes every sheet document's bytes.
// Unmapped members are rejected so a typo in a hand-authored document fails the build, not later.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SpriteSheetJson
{
    // Nullable so an omitted version is distinct from an unsupported numeric version.
    [JsonPropertyName("formatVersion")]
    [JsonPropertyOrder(0)]
    public int? FormatVersion { get; set; }

    [JsonPropertyName("texture")]
    [JsonPropertyOrder(1)]
    public string? Texture { get; set; }

    // Nullable so an absent list is distinct from an empty one, and so is a null where an entry
    // belongs: an initializer here would answer for JSON the format has not accepted.
    [JsonPropertyName("frames")]
    [JsonPropertyOrder(2)]
    public SpriteSheetFrameJson?[]? Frames { get; set; }

    [JsonPropertyName("clips")]
    [JsonPropertyOrder(3)]
    public SpriteSheetClipJson?[]? Clips { get; set; }

    [JsonPropertyName("source")]
    [JsonPropertyOrder(4)]
    public SpriteSheetSourceJson? Source { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SpriteSheetFrameJson
{
    [JsonPropertyName("name")]
    [JsonPropertyOrder(0)]
    public string? Name { get; set; }

    // Nullable so an omitted coordinate or extent fails as the format's missing-field error rather
    // than reading as 0, which is a legal origin and an illegal size.
    [JsonPropertyName("x")]
    [JsonPropertyOrder(1)]
    public int? X { get; set; }

    [JsonPropertyName("y")]
    [JsonPropertyOrder(2)]
    public int? Y { get; set; }

    [JsonPropertyName("width")]
    [JsonPropertyOrder(3)]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    [JsonPropertyOrder(4)]
    public int? Height { get; set; }

    // Absent on a frame anchored at its top-left corner, which is what Sprite.Pivot defaults to;
    // WhenWritingNull keeps it out. An array rather than two members because it is one quantity
    // per axis, and nullable so a wrong arity is the reader's error to name.
    [JsonPropertyName("pivot")]
    [JsonPropertyOrder(5)]
    public float[]? Pivot { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SpriteSheetClipJson
{
    [JsonPropertyName("name")]
    [JsonPropertyOrder(0)]
    public string? Name { get; set; }

    // Nullable so the writer can leave a non-looping clip's flag out entirely.
    [JsonPropertyName("loop")]
    [JsonPropertyOrder(1)]
    public bool? Loop { get; set; }

    [JsonPropertyName("frames")]
    [JsonPropertyOrder(2)]
    public SpriteSheetClipFrameJson?[]? Frames { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SpriteSheetClipFrameJson
{
    [JsonPropertyName("frame")]
    [JsonPropertyOrder(0)]
    public string? Frame { get; set; }

    // Nullable so an omitted duration fails as the format's missing-field error rather than reading
    // as a zero-tick frame.
    [JsonPropertyName("ticks")]
    [JsonPropertyOrder(1)]
    public int? Ticks { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SpriteSheetSourceJson
{
    [JsonPropertyName("tool")]
    [JsonPropertyOrder(0)]
    public string? Tool { get; set; }

    [JsonPropertyName("path")]
    [JsonPropertyOrder(1)]
    public string? Path { get; set; }

    [JsonPropertyName("hash")]
    [JsonPropertyOrder(2)]
    public string? Hash { get; set; }
}
