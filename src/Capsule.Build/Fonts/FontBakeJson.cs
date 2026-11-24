using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Build.Fonts;

// The document as the JSON spells it. Every member is optional here. A missing one is then refused by name
// instead of as a parse failure, and a member the format does not declare fails the font.
[Description("A font baked at one size from a TrueType or OpenType face: printable ASCII, Latin-1, every character the game's string tables use, and any further characters.")]
internal sealed class FontBakeJson
{
    /// <summary>What a refusal calls a baked font's members.</summary>
    internal const string Members = "member";

    /// <summary>Every member a baked font may hold, as a refusal lists them.</summary>
    internal const string Shape = "A baked font holds \"face\", a .ttf or .otf file beside it, and \"size\", its em in pixels. "
        + "It may hold \"fallback\", more faces, \"characters\", a string, and \"antialias\" and \"tables\", each true or false.";

    /// <summary>The largest em a font bakes at, in pixels.</summary>
    internal const int LargestSize = 512;

    // Read and ignored.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    [Description("The face, a .ttf or .otf file, relative to this file. The face itself never ships.")]
    [Required]
    public string? Face { get; set; }

    [Description("The em in canvas pixels, the size a pixel font is drawn on its grid at.")]
    [Required]
    [Range(1, LargestSize)]
    public int? Size { get; set; }

    [Description("Faces relative to this file, in order, each supplying the characters the faces before it lack. Line spacing is the first face's.")]
    public List<string>? Fallback { get; set; }

    [Description("Further characters to bake, for text the string tables do not hold.")]
    public string? Characters { get; set; }

    [Description("Whether glyph edges blend. False keeps every pixel whole, as a pixel font drawn at its design size wants.")]
    [DefaultValue(true)]
    public bool? Antialias { get; set; }

    [Description("Whether every character the game's string tables use is baked. False bakes printable ASCII, Latin-1 and \"characters\" alone, as a display font wants.")]
    [DefaultValue(true)]
    public bool? Tables { get; set; }
}

// Reflection-based serialization is off solution-wide. A member the format does not declare fails the font.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(FontBakeJson))]
internal sealed partial class FontBakeJsonContext : JsonSerializerContext;
