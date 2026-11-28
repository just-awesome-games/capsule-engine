using System.Text;

namespace Capsule.Build.Registry;

/// <summary>Writes the block of a generated <c>public static class</c>, which every nested class of generated members is.</summary>
internal static class GeneratedClass
{
    /// <summary>Opens the class at <paramref name="indent"/> under its summary, and returns its members' indent.</summary>
    /// <param name="attribute">An attribute the class carries, or null.</param>
    internal static string Open(StringBuilder code, string indent, string summary, string identifier, string? attribute = null)
    {
        code.Append(indent).Append("/// <summary>").Append(summary).AppendLine("</summary>");
        if (attribute is not null)
        {
            code.Append(indent).AppendLine(attribute);
        }

        code.Append(indent).Append("public static class ").AppendLine(identifier);
        code.Append(indent).AppendLine("{");

        return indent + "    ";
    }

    /// <summary>Closes the class <see cref="Open"/> opened at <paramref name="indent"/>.</summary>
    internal static void Close(StringBuilder code, string indent) => code.Append(indent).AppendLine("}");
}
