using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

internal static class DeclarationOrder
{
    // Collected models arrive in whichever order the compiler handed the syntax trees over, so the
    // partial declaration a fault is reported against — and the emitted file — would otherwise
    // depend on that. Name, then source path, then span.
    internal static int Compare(string leftName, Location leftLocation, string rightName, Location rightLocation)
    {
        int byName = string.CompareOrdinal(leftName, rightName);
        if (byName != 0)
        {
            return byName;
        }

        int byPath = string.CompareOrdinal(
            leftLocation.SourceTree?.FilePath ?? string.Empty,
            rightLocation.SourceTree?.FilePath ?? string.Empty);

        return byPath != 0
            ? byPath
            : leftLocation.SourceSpan.Start.CompareTo(rightLocation.SourceSpan.Start);
    }
}
