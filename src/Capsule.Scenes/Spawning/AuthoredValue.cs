using System.Text;
using System.Text.Json;

namespace Capsule.Scenes.Spawning;

// Reads one authored JSON value from its exact UTF-8, which the document reader has already validated. Nothing
// here builds a document. An array reads straight from its bytes into the array its read fills.
internal static class AuthoredValue
{
    internal static JsonValueKind Kind(ReadOnlySpan<byte> utf8) => utf8[0] switch
    {
        (byte)'{' => JsonValueKind.Object,
        (byte)'[' => JsonValueKind.Array,
        (byte)'"' => JsonValueKind.String,
        (byte)'t' => JsonValueKind.True,
        (byte)'f' => JsonValueKind.False,
        (byte)'n' => JsonValueKind.Null,
        _ => JsonValueKind.Number,
    };

    internal static bool TryInt(ReadOnlySpan<byte> utf8, out int read)
    {
        read = 0;
        Utf8JsonReader reader = new(utf8);

        return reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out read);
    }

    // A number that converts to a finite float.
    internal static bool TryFloat(ReadOnlySpan<byte> utf8, out float read)
    {
        Utf8JsonReader reader = new(utf8);
        read = reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out double number) ? (float)number : float.NaN;

        return float.IsFinite(read);
    }

    // An array of exactly read.Length finite numbers.
    internal static bool TryFloats(ReadOnlyMemory<byte> utf8, Span<float> read)
    {
        if (Kind(utf8.Span) != JsonValueKind.Array)
        {
            return false;
        }

        Elements elements = new(utf8);
        int count = 0;
        while (elements.Next(out ReadOnlyMemory<byte> element))
        {
            if (count == read.Length || !TryFloat(element.Span, out read[count]))
            {
                return false;
            }

            count++;
        }

        return count == read.Length;
    }

    // The text of a string value, or null for any other value.
    internal static string? String(ReadOnlySpan<byte> utf8)
    {
        if (Kind(utf8) != JsonValueKind.String)
        {
            return null;
        }

        Utf8JsonReader reader = new(utf8);
        reader.Read();

        return reader.GetString();
    }

    internal static int Count(ReadOnlyMemory<byte> array)
    {
        Elements elements = new(array);
        int count = 0;
        while (elements.Next(out _))
        {
            count++;
        }

        return count;
    }

    // The value as written, for a number, true or false.
    internal static string Text(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8);

    // Walks the elements of one array value, handing back each element's exact UTF-8.
    internal ref struct Elements
    {
        private readonly ReadOnlyMemory<byte> _array;
        private Utf8JsonReader _reader;

        internal Elements(ReadOnlyMemory<byte> array)
        {
            _array = array;
            _reader = new Utf8JsonReader(array.Span);
            _reader.Read();
        }

        internal bool Next(out ReadOnlyMemory<byte> element)
        {
            if (!_reader.Read() || _reader.TokenType == JsonTokenType.EndArray)
            {
                element = default;

                return false;
            }

            int start = (int)_reader.TokenStartIndex;
            _reader.Skip();
            element = _array[start..(int)_reader.BytesConsumed];

            return true;
        }
    }
}
