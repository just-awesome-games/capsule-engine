using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Capsule.Assets;
using Capsule.Build.Schemas;

namespace Capsule.Build.Sheets;

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
    [SchemaLength(1)]
    public List<ClipFrameJson>? Frames { get; set; }
}
