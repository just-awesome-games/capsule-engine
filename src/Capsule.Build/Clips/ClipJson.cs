using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Build.Clips;

// The document as the JSON spells it. Every member is optional here. A missing one is then refused
// by name instead of as a parse failure, and a member the format does not declare fails the clip.
[Description("A value clip: an ordered run of numbers, each lasting a whole number of fixed steps, stepped or blended linearly, played once or on a loop.")]
internal sealed class ClipJson
{
    /// <summary>What a refusal calls a clip's members and values.</summary>
    internal const string Members = "member or value";

    /// <summary>Every member a clip and its keys may hold, as a refusal lists them.</summary>
    internal const string Shape = "A clip holds \"formatVersion\" and \"keys\", and may hold \"loop\" (true or false) and \"blend\" (\"step\" or \"linear\"). "
        + "A key holds \"value\", a number, and \"ticks\", the fixed steps it lasts.";

    /// <summary>The document format version this build reads.</summary>
    internal const int SupportedFormat = 1;

    /// <summary>The fewest keys a clip holds.</summary>
    internal const int NonEmpty = 1;

    /// <summary>The fewest fixed steps a key lasts.</summary>
    internal const int LeastTicks = 1;

    // Read and ignored.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    [Description("The document format's version, which must be one this build supports.")]
    [Required]
    [AllowedValues(SupportedFormat)]
    public int? FormatVersion { get; set; }

    [Description("Whether the last key wraps back to the first. Absent plays the clip once and holds its last key.")]
    [DefaultValue(false)]
    public bool? Loop { get; set; }

    [Description("How the value moves between keys. \"step\" shows each key's value whole for its ticks. \"linear\" moves from each key's value toward the next across its ticks, and the last key holds unless the clip loops back to the first.")]
    public ClipBlend? Blend { get; set; }

    [Description("The keys the clip plays in order, at least one.")]
    [Required]
    [SchemaLength(NonEmpty)]
    public List<ClipKeyJson>? Keys { get; set; }
}

[Description("One key of a clip: a value and how many fixed steps it lasts.")]
internal sealed class ClipKeyJson
{
    [Description("The key's value, a finite number.")]
    [Required]
    public float? Value { get; set; }

    [Description("The fixed steps the key lasts, at least one. Not milliseconds.")]
    [Required]
    [Range(ClipJson.LeastTicks, int.MaxValue)]
    public int? Ticks { get; set; }
}

/// <summary>How a clip's value moves between keys, as the file spells it.</summary>
[JsonConverter(typeof(LowerCaseEnumConverter<ClipBlend>))]
internal enum ClipBlend
{
    /// <summary>Each key's value whole for its ticks.</summary>
    Step,

    /// <summary>From each key's value toward the next across its ticks.</summary>
    Linear,
}

// Reflection-based serialization is off solution-wide. A member the format does not declare fails the clip.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ClipJson))]
internal sealed partial class ClipJsonContext : JsonSerializerContext;
