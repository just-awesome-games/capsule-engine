using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Capsule.Build.Sheets;

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
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)]
    public float? Width { get; set; }

    [Description("The box's height in texels, positive.")]
    [Required]
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)]
    public float? Height { get; set; }
}
