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

        string? leftPath = leftLocation.SourceTree?.FilePath;
        string? rightPath = rightLocation.SourceTree?.FilePath;
        int byPath = string.CompareOrdinal(leftPath ?? string.Empty, rightPath ?? string.Empty);
        if (byPath != 0)
        {
            return byPath;
        }

        if ((leftPath is null) != (rightPath is null))
        {
            return leftPath is null ? 1 : -1;
        }

        return leftLocation.SourceSpan.Start.CompareTo(rightLocation.SourceSpan.Start);
    }
}
