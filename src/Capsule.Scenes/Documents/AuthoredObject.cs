using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Capsule.Scenes;

// One authored JSON object as its keys and the exact UTF-8 of each key's value, sliced from bytes the reader has
// already validated. A repeated key keeps its first place and its last value. Nothing is parsed until a read
// asks for a value. A large array costs only its own bytes until then.
internal sealed class AuthoredObject
{
    // Shared with the object its KeyTable built before it when both author the same keys in the same order.
    private readonly string[] _keys;
    private readonly ReadOnlyMemory<byte>[] _values;

    // What a read made of each key's value, which every later composition of the document reuses, or null until
    // the first read keeps one. Most objects keep none.
    private object?[]? _reads;

    // What the object's class built from every member together, which every later composition reuses.
    private object? _whole;

    private AuthoredObject(string[] keys, ReadOnlyMemory<byte>[] values)
    {
        _keys = keys;
        _values = values;
    }

    // Every key, in authored order, each once.
    internal string[] Keys => _keys;

    // The keys and values of one object read off reader, which sits on its StartObject, skipping every key the
    // table reserves. The values are slices of utf8, the bytes reader reads. Returns null when no key is left.
    internal static AuthoredObject? Read(ref Utf8JsonReader reader, ReadOnlyMemory<byte> utf8, KeyTable keys)
    {
        keys.Begin();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            keys.Add(ref reader, utf8);
        }

        return keys.Build();
    }

    // The object utf8 holds, which is exactly one validated JSON object. An object parsed through a table shares
    // the keys of the one before it when they match.
    internal static AuthoredObject Parse(ReadOnlyMemory<byte> utf8, KeyTable? keys = null)
    {
        Utf8JsonReader reader = new(utf8.Span);
        reader.Read();

        return Read(ref reader, utf8, keys ?? new KeyTable()) ?? new AuthoredObject([], []);
    }

    // The members an element authors, read from a copy of its bytes. The element must be an object.
    internal static AuthoredObject From(JsonElement members) => Parse(JsonMarshal.GetRawUtf8Value(members).ToArray());

    internal bool TryGetValue(string key, out ReadOnlyMemory<byte> value)
    {
        int index = IndexOf(key);
        value = index >= 0 ? _values[index] : default;

        return index >= 0;
    }

    // The place of key among Keys, or -1 when the object does not author it.
    internal int IndexOf(string key) => Array.IndexOf(_keys, key);

    internal ReadOnlyMemory<byte> ValueAt(int index) => _values[index];

    // What a read kept of key's value, or null when none has.
    internal object? Kept(string key) => _reads?[IndexOf(key)];

    // Keeps read for key unless a racing composition kept one first, and returns the one kept. Every caller
    // then holds one instance.
    internal object Keep(string key, object read)
    {
        object?[] reads = _reads ?? Interlocked.CompareExchange(ref _reads, new object?[_keys.Length], null) ?? _reads;

        return Interlocked.CompareExchange(ref reads[IndexOf(key)], read, null) ?? read;
    }

    // What a composition kept of the whole object, or null when none has.
    internal object? Whole => _whole;

    // Keeps built for the whole object unless a racing composition kept one first, and returns the one kept.
    internal object KeepWhole(object built) => Interlocked.CompareExchange(ref _whole, built, null) ?? built;

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

    // Builds objects one at a time and keeps each key name once. An object that authors the same keys as the one
    // built before it shares that one's array of them. A name is checked against the reserved keys once, when the
    // table first meets it.
    internal sealed class KeyTable
    {
        // A name this long or shorter is looked up without allocating.
        private const int StackName = 128;

        private readonly Dictionary<string, (string Name, bool Reserved)> _names = new(StringComparer.Ordinal);

        // The object being built.
        private readonly List<string> _keys = [];
        private readonly List<ReadOnlyMemory<byte>> _values = [];

        // The keys of the last object built that authored any.
        private string[]? _last;

        // A table whose objects take no key in reserved. Each reserved name reads as the instance reserved holds.
        internal KeyTable(params string[] reserved)
        {
            foreach (string name in reserved)
            {
                _names[name] = (name, true);
            }
        }

        internal void Begin()
        {
            _keys.Clear();
            _values.Clear();
        }

        // Takes the property reader sits on, unless the table reserves it, and reads past its value, a slice of utf8.
        internal void Add(ref Utf8JsonReader reader, ReadOnlyMemory<byte> utf8)
        {
            (string key, bool reserved) = Name(ref reader);
            reader.Read();
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            if (reserved)
            {
                return;
            }

            ReadOnlyMemory<byte> value = utf8[start..(int)reader.BytesConsumed];

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

        // What was added since Begin, or null when nothing was.
        internal AuthoredObject? Build()
        {
            if (_keys.Count == 0)
            {
                return null;
            }

            ReadOnlySpan<string> keys = CollectionsMarshal.AsSpan(_keys);
            string[] shared = _last is { } last && keys.SequenceEqual(last) ? last : (_last = keys.ToArray());

            return new AuthoredObject(shared, [.. _values]);
        }

        // The property name reader sits on, unescaped, as the one instance the table keeps for it, and whether
        // the table reserves it.
        private (string Name, bool Reserved) Name(ref Utf8JsonReader reader)
        {
            // A name never unescapes or transcodes to more chars than it has bytes.
            if (reader.ValueSpan.Length > StackName)
            {
                return Intern(reader.GetString()!);
            }

            Span<char> chars = stackalloc char[StackName];
            ReadOnlySpan<char> name = chars[..reader.CopyString(chars)];

            return _names.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out (string, bool) kept)
                ? kept
                : Intern(new string(name));
        }

        private (string Name, bool Reserved) Intern(string name)
        {
            ref (string Name, bool Reserved) kept = ref CollectionsMarshal.GetValueRefOrAddDefault(_names, name, out bool exists);
            if (!exists)
            {
                kept = (name, false);
            }

            return kept;
        }
    }
}
