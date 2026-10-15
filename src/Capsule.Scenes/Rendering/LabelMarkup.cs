namespace Capsule.Rendering;

// One bracketed tag of Label.RichText markup: [name], [name=value] or [/name]. Value is empty where the tag
// gives none, and a closing tag never has one.
internal readonly ref struct MarkupTag
{
    internal ReadOnlySpan<char> Name { get; init; }

    internal ReadOnlySpan<char> Value { get; init; }

    internal bool Closing { get; init; }
}

// The syntax of Label.RichText markup, apart from what any tag means. A tag is the text from a '[' to the
// next ']' with no '[' between them, and "[[" is one literal '['.
internal static class LabelMarkup
{
    // Reads the tag text starts with, and how many chars it spans. False where text does not start with one.
    internal static bool TryReadTag(ReadOnlySpan<char> text, out MarkupTag tag, out int length)
    {
        tag = default;
        length = 0;

        if (text.IsEmpty || text[0] != '[')
        {
            return false;
        }

        int end = text[1..].IndexOfAny('[', ']');
        if (end < 0 || text[1 + end] != ']')
        {
            return false;
        }

        ReadOnlySpan<char> body = text.Slice(1, end);
        length = end + 2;

        if (body.StartsWith('/'))
        {
            tag = new MarkupTag { Name = body[1..], Closing = true };

            return true;
        }

        int equals = body.IndexOf('=');
        tag = equals < 0 ? new MarkupTag { Name = body } : new MarkupTag { Name = body[..equals], Value = body[(equals + 1)..] };

        return true;
    }

    // Whether text starts with "[[", which draws one '['.
    internal static bool IsEscape(ReadOnlySpan<char> text) => text.StartsWith("[[", StringComparison.Ordinal);
}
