using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Capsule.Assets;

namespace Capsule.Build.Sheets;

[Description("One step of a clip: a frame and how long it is held.")]
internal sealed class ClipFrameJson
{
    [Description("The name of a frame of this sheet.")]
    [Required]
    [SchemaLength(1)]
    public string? Frame { get; set; }

    [Description("The fixed steps the frame is held for, at least one. Not milliseconds.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int? Ticks { get; set; }

    [Description("The declared events this entry raises as it starts, each listed once. Absent is none.")]
    public List<string>? Events { get; set; }
}
