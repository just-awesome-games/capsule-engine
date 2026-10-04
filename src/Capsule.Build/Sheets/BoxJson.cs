using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Capsule.Build.Schemas;

namespace Capsule.Build.Sheets;

[Description("A named rect the frames set, such as a hurtbox or a spike strip.")]
internal sealed class BoxJson
{
    [Description("The box's name, unique among the boxes. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }
}
