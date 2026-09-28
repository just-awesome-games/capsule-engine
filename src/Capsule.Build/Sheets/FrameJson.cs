using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Capsule.Assets;
using Capsule.Build.Schemas;

namespace Capsule.Build.Sheets;

[Description("One region of the sheet's texture, with the pivot and sockets it carries.")]
internal sealed class FrameJson
{
    [Description("The frame's name, unique among the frames. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }

    [Description("The frame's left edge in texels of the texture.")]
    [Required]
    [Range(0, int.MaxValue)]
    public int? X { get; set; }

    [Description("The frame's top edge in texels of the texture.")]
    [Required]
    [Range(0, int.MaxValue)]
    public int? Y { get; set; }

    [Description("The frame's width in texels.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int? Width { get; set; }

    [Description("The frame's height in texels.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int? Height { get; set; }

    [Description("The pivot as [x, y] in texels of the frame from its own top-left corner, both finite. Absent is that corner.")]
    [SchemaLength(2, 2)]
    public float[]? Pivot { get; set; }

    [Description("A declared socket's name mapped to its point on this frame, [x, y] in the pivot's texel space. A frame sets the sockets it has a point for and leaves the rest out.")]
    public Dictionary<string, float[]>? Sockets { get; set; }
}
