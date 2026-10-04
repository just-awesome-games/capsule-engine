using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Capsule.Build.Schemas;

namespace Capsule.Build.Sheets;

[Description("A named moment the clip entries raise, such as a footstep or a swing.")]
internal sealed class EventJson
{
    [Description("The event's name, unique among the events. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }
}
