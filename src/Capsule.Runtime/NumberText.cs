using System.Globalization;

namespace Capsule.Runtime;

// A line of figures formatted into a buffer the caller owns, which lets a readout and a CSV row be
// rewritten every frame with no allocation. Numbers are invariant. Text past the buffer's end
// is cut, and the buffer never grows.
internal ref struct NumberText
{
    private readonly Span<char> _buffer;
    private int _length;

    internal NumberText(Span<char> buffer) => _buffer = buffer;

    internal ReadOnlySpan<char> Written => _buffer[.._length];

    internal void Add(scoped ReadOnlySpan<char> text)
    {
        Span<char> free = _buffer[_length..];
        if (text.Length > free.Length)
        {
            text = text[..free.Length];
        }

        text.CopyTo(free);
        _length += text.Length;
    }

    // Right-aligned in width columns, or its own width where that is wider.
    internal void Add(double value, string format, int width = 0)
    {
        Span<char> digits = stackalloc char[32];
        value.TryFormat(digits, out int written, format, CultureInfo.InvariantCulture);
        Pad(digits[..written], width);
    }

    internal void Add(int value, int width = 0)
    {
        Span<char> digits = stackalloc char[16];
        value.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture);
        Pad(digits[..written], width);
    }

    private void Pad(scoped ReadOnlySpan<char> figure, int width)
    {
        for (int pad = figure.Length; pad < width; pad++)
        {
            Add(" ");
        }

        Add(figure);
    }
}
