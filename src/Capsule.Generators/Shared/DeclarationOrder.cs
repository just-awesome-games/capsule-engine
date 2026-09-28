namespace Capsule.Generators;

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
