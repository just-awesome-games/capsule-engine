using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Capsule.Build.Schemas;

namespace Capsule.Build.Sheets;

[Description("A named point the frames set, such as a muzzle or a hand.")]
internal sealed class SocketJson
{
    [Description("The socket's name, unique among the sockets. Letters, digits, '-' and '_', not starting with a digit.")]
    [Required]
    [RegularExpression(FormatSchemas.NamePattern)]
    public string? Name { get; set; }
}
