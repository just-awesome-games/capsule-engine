using System.ComponentModel;

namespace Capsule.Build.Sheets;

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
