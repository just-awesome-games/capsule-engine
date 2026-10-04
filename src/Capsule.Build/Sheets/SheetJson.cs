using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Capsule.Assets;

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

    // Read and ignored.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    [Description("The document format's version, which must be one this build supports.")]
    [Required]
    [AllowedValues(SheetFile.SupportedFormat)]
    public int? FormatVersion { get; set; }

    [Description("The key of the texture every frame is cut from, extension included, with forward slashes and no empty, \".\" or \"..\" segment. Any spelling of the key reads.")]
    [Required]
    [SchemaLength(1)]
    public string? Texture { get; set; }

    [Description("The sockets the frames set. Absent or empty generates no Sockets class.")]
    public List<SocketJson>? Sockets { get; set; }

    [Description("The boxes the frames set. Absent or empty generates no Boxes class.")]
    public List<BoxJson>? Boxes { get; set; }

    [Description("The events the clip entries raise. Absent or empty generates no Events class.")]
    public List<EventJson>? Events { get; set; }

    [Description("The regions of the texture the sheet names, at least one.")]
    [Required]
    [SchemaLength(1)]
    public List<FrameJson>? Frames { get; set; }

    [Description("The animations played over the sheet's frames. Absent or empty is a sheet of frames only, with no Clips class generated.")]
    public List<ClipJson>? Clips { get; set; }

    [Description("What a derived sheet came from. Nothing reads it.")]
    public SourceJson? Source { get; set; }
}
