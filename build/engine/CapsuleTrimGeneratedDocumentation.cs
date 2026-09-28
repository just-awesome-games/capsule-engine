// The task Capsule.Documentation.targets runs after compilation. MSBuild compiles this file through
// RoslynCodeTaskFactory, and no project includes it.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

public sealed class CapsuleTrimGeneratedDocumentation : Microsoft.Build.Utilities.Task
{
    public string DocumentationPath { get; set; }

    public string[] GeneratedInternalTypes { get; set; }

    public override bool Execute()
    {
        XDocument document = XDocument.Load(DocumentationPath, LoadOptions.PreserveWhitespace);
        XElement[] generated = document.Descendants("member").Where(IsGenerated).ToArray();

        foreach (XElement entry in generated)
        {
            // The whitespace ahead of an entry belongs to it; leaving it behind would blank a line.
            if (entry.PreviousNode is XText indent && indent.Value.Trim().Length == 0)
            {
                indent.Remove();
            }

            entry.Remove();
        }

        // An untouched file keeps its timestamp, so a rebuild that skipped compilation stays a no-op.
        if (generated.Length > 0)
        {
            using (StreamWriter writer = new StreamWriter(DocumentationPath, false, new UTF8Encoding(false)))
            {
                document.Save(writer, SaveOptions.DisableFormatting);
            }
        }

        return true;
    }

    // An identifier is a kind letter, a colon, then the type, one of its members, or a nested type.
    private bool IsGenerated(XElement entry)
    {
        string name = (string)entry.Attribute("name");
        if (name == null || name.Length < 3)
        {
            return false;
        }

        string subject = name.Substring(2);
        return GeneratedInternalTypes.Any(type => subject == type || subject.StartsWith(type + ".", StringComparison.Ordinal));
    }
}
