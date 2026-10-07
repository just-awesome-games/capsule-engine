using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Capsule.Scenes.Documents;

// One authored JSON object as its keys and the exact UTF-8 of each key's value, sliced from bytes the reader has
// already validated. A repeated key keeps its first place and its last value. Nothing is parsed until a read
// asks for a value. A large array costs only its own bytes until then.
internal sealed class AuthoredObject
{
    private readonly string[] _keys;
    private readonly ReadOnlyMemory<byte>[] _values;

    // What a read made of each key's value, which every later composition of the document reuses.
    private readonly object?[] _reads;

    private AuthoredObject(string[] keys, ReadOnlyMemory<byte>[] values)
    {
        _keys = keys;
        _values = values;
        _reads = new object?[keys.Length];
    }

    // Every key, in authored order, each once.
    internal string[] Keys => _keys;

    // The keys and values of one object read off reader, which sits on its StartObject, skipping every key in
    // reserved. The values are slices of utf8, the bytes reader reads. Returns null when no key is left.
    internal static AuthoredObject? Read(ref Utf8JsonReader reader, ReadOnlyMemory<byte> utf8, string[] reserved)
    {
        Builder members = default;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (Named(ref reader, reserved))
            {
                reader.Read();
                reader.Skip();
            }
            else
            {
                members.Add(ref reader, utf8);
            }
        }

        return members.Build();
    }

    // Whether the property name reader sits on is one of keys, compared after unescaping.
    internal static bool Named(ref Utf8JsonReader reader, string[] keys)
    {
        foreach (string key in keys)
        {
            if (reader.ValueTextEquals(key))
            {
                return true;
            }
        }

        return false;
    }

    // The object utf8 holds, which is exactly one validated JSON object.
    internal static AuthoredObject Parse(ReadOnlyMemory<byte> utf8)
    {
        Utf8JsonReader reader = new(utf8.Span);
        reader.Read();

        return Read(ref reader, utf8, []) ?? new AuthoredObject([], []);
    }

    // The members an element authors, read from a copy of its bytes. The element must be an object.
    internal static AuthoredObject From(JsonElement members) => Parse(JsonMarshal.GetRawUtf8Value(members).ToArray());

    internal bool TryGetValue(string key, out ReadOnlyMemory<byte> value)
    {
        int index = Array.IndexOf(_keys, key);
        value = index >= 0 ? _values[index] : default;

        return index >= 0;
    }

    // What a read kept of key's value, or null when none has.
    internal object? Kept(string key) => _reads[Array.IndexOf(_keys, key)];

    // Keeps read for key unless a racing composition kept one first, and returns the one kept. Every caller
    // then holds one instance.
    internal object Keep(string key, object read) =>
        Interlocked.CompareExchange(ref _reads[Array.IndexOf(_keys, key)], read, null) ?? read;

    // The members as a new JSON object, each key once.
    internal JsonElement ToElement()
    {
        ArrayBufferWriter<byte> written = new();
        using (Utf8JsonWriter writer = new(written))
        {
            writer.WriteStartObject();
            for (int i = 0; i < _keys.Length; i++)
            {
                writer.WritePropertyName(_keys[i]);
                writer.WriteRawValue(_values[i].Span, skipInputValidation: true);
            }

            writer.WriteEndObject();
        }

        Utf8JsonReader reader = new(written.WrittenSpan);

        return JsonElement.ParseValue(ref reader);
    }

    // Collects an object's keys and values one property at a time.
    internal struct Builder
    {
        private List<string>? _keys;
        private List<ReadOnlyMemory<byte>>? _values;

        // Takes the property reader sits on and reads past its value, a slice of utf8.
        internal void Add(ref Utf8JsonReader reader, ReadOnlyMemory<byte> utf8)
        {
            string key = reader.GetString()!;
            reader.Read();
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            ReadOnlyMemory<byte> value = utf8[start..(int)reader.BytesConsumed];

            _keys ??= [];
            _values ??= [];
            int earlier = _keys.IndexOf(key);
            if (earlier >= 0)
            {
                _values[earlier] = value;
            }
            else
            {
                _keys.Add(key);
                _values.Add(value);
            }
        }

        // What was added, or null when nothing was.
        internal readonly AuthoredObject? Build() => _keys is null ? null : new AuthoredObject([.. _keys], [.. _values!]);
    }
}
