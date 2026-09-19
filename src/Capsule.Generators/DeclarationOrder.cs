using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

/// <summary>
/// A declaration's position as plain text instead of a <see cref="Location"/>. A cached Location
/// would keep its syntax tree alive for the life of the generator's cache.
/// </summary>
internal readonly record struct DeclaredAt(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    internal static DeclaredAt From(Location location)
    {
        FileLinePositionSpan lines = location.GetLineSpan();

        return new DeclaredAt(lines.Path, location.SourceSpan, lines.Span);
    }

    /// <summary>Rebuilds the location to report a diagnostic at.</summary>
    internal Location Location() => Microsoft.CodeAnalysis.Location.Create(FilePath, Span, LineSpan);
}

internal static class DeclarationOrder
{
    // Collected models arrive in the compiler's syntax-tree order, which is not stable across
    // builds. Order by name, then source path, then span.
    internal static int Compare(string leftName, DeclaredAt left, string rightName, DeclaredAt right)
    {
        int byName = string.CompareOrdinal(leftName, rightName);
        if (byName != 0)
        {
            return byName;
        }

        int byPath = string.CompareOrdinal(left.FilePath, right.FilePath);

        return byPath != 0 ? byPath : left.Span.Start.CompareTo(right.Span.Start);
    }
}
