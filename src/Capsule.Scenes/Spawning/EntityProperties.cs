using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Rendering;
using Capsule.Scenes.Documents;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// The <c>properties</c> object of one scene-document entry, which its class's generated applier reads
/// into the <see cref="AuthorableAttribute"/> members by key.
/// </summary>
/// <remarks>
/// A read that meets an absent key, a JSON null or the wrong JSON throws
/// <see cref="SceneDocumentFormatException"/> naming the entry, the key and the form to write. The
/// applier checks <see cref="Has"/> before reading an optional member and <see cref="IsNull"/> before
/// reading a nullable one.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct EntityProperties
{
    // Plain options with no reflection-based resolver, which a trimmed or ahead-of-time build keeps.
    private static readonly JsonSerializerOptions ConverterOptions = new();

    private readonly EntityPlacement _entry;

    // Every game entry of the document by id, which a reference resolves against, or null before all are constructed.
    private readonly Dictionary<int, Entity>? _placed;

    internal EntityProperties(EntityPlacement entry, Dictionary<int, Entity>? placed = null)
    {
        _entry = entry;
        _placed = placed;
    }

    /// <summary>Whether the entry authors <paramref name="key"/>, as a JSON null included.</summary>
    public bool Has(string key) => Find(key, out _);

    /// <summary>Whether the entry authors <paramref name="key"/> as a JSON null.</summary>
    public bool IsNull(string key) => Find(key, out JsonElement value) && value.ValueKind == JsonValueKind.Null;

    /// <summary>Reads a <see langword="bool"/>, written <c>true</c> or <c>false</c>.</summary>
    public bool Bool(string key) => Authored(key).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Mismatch(key, "bool", "true or false"),
    };

    /// <summary>Reads an <see langword="int"/>, written as a whole number in range.</summary>
    public int Int(string key) =>
        Authored(key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out int read)
            ? read
            : throw Mismatch(key, "int", "a whole number from -2147483648 to 2147483647");

    /// <summary>Reads a <see langword="float"/>, written as a finite number.</summary>
    public float Float(string key) =>
        TryFloat(Authored(key), out float read) ? read : throw Mismatch(key, "float", "a finite number");

    /// <summary>Reads a <see langword="string"/>.</summary>
    public string String(string key) =>
        Authored(key) is { ValueKind: JsonValueKind.String } value
            ? value.GetString()!
            : throw Mismatch(key, "string", "a string in quotes");

    /// <summary>Reads a <see cref="System.Numerics.Vector2"/>, written <c>[x, y]</c> with both finite.</summary>
    public Vector2 Vector2(string key) =>
        Authored(key) is { ValueKind: JsonValueKind.Array } value
        && value.GetArrayLength() == 2
        && TryFloat(value[0], out float x)
        && TryFloat(value[1], out float y)
            ? new Vector2(x, y)
            : throw Mismatch(key, "Vector2", "[x, y] with both numbers finite");

    /// <summary>Reads a <see cref="ColorRgba"/>, written <c>"#rrggbb"</c> or <c>"#rrggbbaa"</c>.</summary>
    public ColorRgba Color(string key)
    {
        const string Form = "\"#rrggbb\" or \"#rrggbbaa\"";
        if (Authored(key) is not { ValueKind: JsonValueKind.String } value)
        {
            throw Mismatch(key, "ColorRgba", Form);
        }

        try
        {
            return ColorRgba.FromHex(value.GetString()!);
        }
        catch (FormatException)
        {
            throw Mismatch(key, "ColorRgba", Form);
        }
    }

    /// <summary>Reads the enum member or definition an entry names, which the applier then matches.</summary>
    public string Name(string key) =>
        Authored(key) is { ValueKind: JsonValueKind.String } value
            ? value.GetString()!
            : throw Mismatch(key, "a name", "a name in quotes");

    /// <summary>The failure for a name that matches none of <paramref name="names"/>.</summary>
    /// <param name="key">The member's key.</param>
    /// <param name="names">Every name the member accepts, comma-joined.</param>
    public SceneDocumentFormatException NotAName(string key, string names) =>
        new($"{Entry} sets '{key}' to {Found(Authored(key))}, which names nothing the member accepts. Write one of: {names}.");

    /// <summary>
    /// Reads a value through <typeparamref name="TConverter"/>, the converter the member's type names with
    /// <see cref="JsonConverterAttribute"/>. A JSON null is refused.
    /// </summary>
    public T Read<T, TConverter>(string key)
        where TConverter : JsonConverter<T>, new()
    {
        JsonElement value = Authored(key);
        if (value.ValueKind == JsonValueKind.Null)
        {
            throw Mismatch(key, typeof(T).Name, "the form " + typeof(TConverter).Name + " reads");
        }

        try
        {
            Utf8JsonReader reader = new(JsonMarshal.GetRawUtf8Value(value));
            reader.Read();

            return new TConverter().Read(ref reader, typeof(T), ConverterOptions)!;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // new() constructs through reflection, which wraps what a throwing constructor throws.
            Exception cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;

            throw new SceneDocumentFormatException(
                $"{Entry} sets '{key}' to {Found(value)}, which {typeof(TConverter).Name} could not read as {typeof(T).Name}: {cause.Message} Write the value in the form that converter reads.",
                cause);
        }
    }

    /// <summary>
    /// Reads the entity an entry id names, which must be a <typeparamref name="T"/> placed by the same
    /// document. The applier of references reads it once every entry is constructed.
    /// </summary>
    public T Entity<T>(string key)
        where T : class
    {
        JsonElement value = Authored(key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int id))
        {
            throw Mismatch(key, typeof(T).Name, "an entity id, a whole number");
        }

        if (_placed is null || !_placed.TryGetValue(id, out Entity? target))
        {
            throw new SceneDocumentFormatException(
                $"{Entry} sets '{key}' to {id}, which names no entity in the document. Write the id of an entity entry.");
        }

        return target as T ?? throw new SceneDocumentFormatException(
            $"{Entry} sets '{key}' to entity {id}, a {target.GetType().Name}, but the member takes {typeof(T).Name}. Write the id of an entity that is a {typeof(T).Name}.");
    }

    private string Entry => $"entity id {_entry.Id} ('{_entry.Type}')";

    private bool Find(string key, out JsonElement value)
    {
        value = default;

        return _entry.Properties is { } properties && properties.TryGetProperty(key, out value);
    }

    // The applier reads an optional member only after Has, so an absent key here is a required member.
    private JsonElement Authored(string key) =>
        Find(key, out JsonElement value)
            ? value
            : throw new SceneDocumentFormatException(
                $"{Entry} omits '{key}', which its class requires. Add \"{key}\" to the entry's properties.");

    private SceneDocumentFormatException Mismatch(string key, string expected, string form)
    {
        JsonElement value = Authored(key);

        return new(value.ValueKind == JsonValueKind.Null
            ? $"{Entry} sets '{key}' to null, which only a nullable member accepts. The member takes {expected}. Write {form}, or make the member nullable."
            : $"{Entry} sets '{key}' to {Found(value)}, but the member takes {expected}. Write {form}.");
    }

    private static bool TryFloat(JsonElement value, out float read)
    {
        read = value.ValueKind == JsonValueKind.Number ? (float)value.GetDouble() : float.NaN;

        return float.IsFinite(read);
    }

    // What the entry wrote, short enough for one line of a message.
    private static string Found(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"the string \"{value.GetString()}\"",
        JsonValueKind.Number => string.Create(CultureInfo.InvariantCulture, $"the number {value.GetRawText()}"),
        JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ => "null",
    };
}
