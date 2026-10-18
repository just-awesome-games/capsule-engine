using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Capsule.Assets;
using Capsule.Build.Schemas;

namespace Capsule.Build.Sheets;

// The document as the JSON spells it. Every member is optional here. A missing one is then refused
// by name instead of as a parse failure, and a member the format does not declare fails the sheet.
[Description("A sprite sheet: the texture it cuts from, the sockets and boxes its frames set, the events its clips raise, its frames and the clips played over them.")]
internal sealed class SheetJson
{
    /// <summary>What a refusal calls a sheet's members and values.</summary>
    internal const string Members = "member or value";

    /// <summary>Every member a sheet and its parts may hold, as a refusal lists them.</summary>
    internal const string Shape = "A sheet holds \"formatVersion\", \"texture\" and \"frames\", and may hold \"sockets\", \"boxes\", \"events\", \"clips\" and \"source\". "
        + "A frame holds \"name\", \"x\", \"y\", \"width\" and \"height\", and may hold \"pivot\", \"sockets\" and \"boxes\". A frame's box holds \"x\", \"y\", \"width\" and \"height\". "
        + "A clip holds \"name\" and \"frames\", each a \"frame\" and its \"ticks\", and may hold \"loop\". An entry may hold \"events\". "
        + "A socket, a box or an event holds \"name\", and \"source\" holds \"tool\", \"path\" and \"hash\".";

    /// <summary>The document format version this build reads.</summary>
    internal const int SupportedFormat = 1;

    /// <summary>The fewest characters or items a value the format requires to be non-empty holds.</summary>
    internal const int NonEmpty = 1;

    /// <summary>The least texel a frame's region starts at on either axis.</summary>
    internal const int LeastOrigin = 0;

    /// <summary>The fewest texels a frame's region spans on either axis.</summary>
    internal const int LeastExtent = 1;

    /// <summary>The fewest fixed steps a clip entry holds its frame for.</summary>
    internal const int LeastTicks = 1;

    /// <summary>The value a box's width and height each exceed.</summary>
    internal const double BoxExtentAbove = 0;

    /// <summary>The components of a point on a frame, written [x, y].</summary>
    internal const int PointLength = 2;

    // Read and ignored.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    [Description("The document format's version, which must be one this build supports.")]
    [Required]
    [AllowedValues(SupportedFormat)]
    public int? FormatVersion { get; set; }

    [Description("The key of the texture every frame is cut from, extension included, with forward slashes and no empty, \".\" or \"..\" segment. Any spelling of the key reads.")]
    [Required]
    [SchemaLength(NonEmpty)]
    public string? Texture { get; set; }

    [Description("The sockets the frames set. Absent or empty generates no Sockets class.")]
    public List<SocketJson>? Sockets { get; set; }

    [Description("The boxes the frames set. Absent or empty generates no Boxes class.")]
    public List<BoxJson>? Boxes { get; set; }

    [Description("The events the clip entries raise. Absent or empty generates no Events class.")]
    public List<EventJson>? Events { get; set; }

    [Description("The regions of the texture the sheet names, at least one.")]
    [Required]
    [SchemaLength(NonEmpty)]
    public List<FrameJson>? Frames { get; set; }

    [Description("The animations played over the sheet's frames. Absent or empty is a sheet of frames only, with no Clips class generated.")]
    public List<ClipJson>? Clips { get; set; }

    [Description("What a derived sheet came from. Nothing reads it.")]
    public SourceJson? Source { get; set; }
}

[Description("A named point the frames set, such as a muzzle or a hand.")]
internal sealed class SocketJson
{
    [Description("The socket's name, unique among the sockets. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }
}

[Description("A named rect the frames set, such as a hurtbox or a spike strip.")]
internal sealed class BoxJson
{
    [Description("The box's name, unique among the boxes. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }
}

[Description("A named moment the clip entries raise, such as a footstep or a swing.")]
internal sealed class EventJson
{
    [Description("The event's name, unique among the events. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }
}

[Description("One region of the sheet's texture, with the pivot, sockets and boxes it carries.")]
internal sealed class FrameJson
{
    [Description("The frame's name, unique among the frames. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }

    [Description("The frame's left edge in texels of the texture.")]
    [Required]
    [Range(SheetJson.LeastOrigin, int.MaxValue)]
    public int? X { get; set; }

    [Description("The frame's top edge in texels of the texture.")]
    [Required]
    [Range(SheetJson.LeastOrigin, int.MaxValue)]
    public int? Y { get; set; }

    [Description("The frame's width in texels.")]
    [Required]
    [Range(SheetJson.LeastExtent, int.MaxValue)]
    public int? Width { get; set; }

    [Description("The frame's height in texels.")]
    [Required]
    [Range(SheetJson.LeastExtent, int.MaxValue)]
    public int? Height { get; set; }

    [Description("The pivot as [x, y] in texels of the frame from its own top-left corner, both finite. Absent is that corner.")]
    [SchemaLength(SheetJson.PointLength, SheetJson.PointLength)]
    public float[]? Pivot { get; set; }

    [Description("A declared socket's name mapped to its point on this frame, [x, y] in the pivot's texel space. A frame sets the sockets it has a point for and leaves the rest out.")]
    public Dictionary<string, float[]>? Sockets { get; set; }

    [Description("A declared box's name mapped to its rect on this frame, in the pivot's texel space. A frame sets the boxes it has a rect for and leaves the rest out.")]
    public Dictionary<string, FrameBoxJson>? Boxes { get; set; }
}

[Description("A box's rect on one frame, in texels from the frame's own top-left corner. It may reach outside the frame.")]
internal sealed class FrameBoxJson
{
    [Description("The box's left edge in texels of the frame, finite.")]
    [Required]
    public float? X { get; set; }

    [Description("The box's top edge in texels of the frame, finite.")]
    [Required]
    public float? Y { get; set; }

    [Description("The box's width in texels, positive.")]
    [Required]
    [Range(SheetJson.BoxExtentAbove, double.MaxValue, MinimumIsExclusive = true)]
    public float? Width { get; set; }

    [Description("The box's height in texels, positive.")]
    [Required]
    [Range(SheetJson.BoxExtentAbove, double.MaxValue, MinimumIsExclusive = true)]
    public float? Height { get; set; }
}

[Description("One animation played over the sheet's own frames.")]
internal sealed class ClipJson
{
    [Description("The clip's name, unique among the clips. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }

    [Description("Whether the last entry wraps back to the first.")]
    [DefaultValue(false)]
    public bool? Loop { get; set; }

    [Description("The frames the clip plays in order, at least one.")]
    [Required]
    [SchemaLength(SheetJson.NonEmpty)]
    public List<ClipFrameJson>? Frames { get; set; }
}

[Description("One step of a clip: a frame and how long it is held.")]
internal sealed class ClipFrameJson
{
    [Description("The name of a frame of this sheet.")]
    [Required]
    [SchemaLength(SheetJson.NonEmpty)]
    public string? Frame { get; set; }

    [Description("The fixed steps the frame is held for, at least one. Not milliseconds.")]
    [Required]
    [Range(SheetJson.LeastTicks, int.MaxValue)]
    public int? Ticks { get; set; }

    [Description("The declared events this entry raises as it starts, each listed once. Absent is none.")]
    public List<string>? Events { get; set; }
}

[Description("What a derived sheet came from: the tool, the source's path and its hash.")]
internal sealed class SourceJson
{
    [Description("The tool that derived the sheet.")]
    public string? Tool { get; set; }

    [Description("The source's path.")]
    public string? Path { get; set; }

    [Description("The source's hash.")]
    public string? Hash { get; set; }
}

// Reflection-based serialization is off solution-wide. A member the format does not declare fails the sheet.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SheetJson))]
internal sealed partial class SheetJsonContext : JsonSerializerContext;
