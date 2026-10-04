using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Rendering;
using Capsule.Scenes.Documents;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// The authorable member values of one scene-document entry, of the document itself or of one object nested in
/// either, which the generated applier of the owner's class reads into the
/// <see cref="AuthorableAttribute"/> members by key.
/// </summary>
/// <remarks>
/// A read that meets an absent key, a JSON null or the wrong JSON throws
/// <see cref="SceneDocumentFormatException"/> naming the entry or the document, the key path and the form to write.
/// The applier checks <see cref="Has"/> before reading an optional member and <see cref="IsNull"/> before
/// reading a nullable one. <see cref="Array{T}"/> hands each element to the applier's read as a
/// value of its own, whose reads take the array's key. Once the owner is composed, a key no read asked for
/// at any level fails the same way.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct AuthoredMembers
{
    private const string TypeKey = SceneDocumentKeys.MemberObject.Type;

    // Plain options with no reflection-based resolver, which a trimmed or ahead-of-time build keeps.
    private static readonly JsonSerializerOptions ConverterOptions = new();

    // What every level of one owner's values shares, or null for a spawn built in code.
    private readonly Owner? _owner;

    private readonly JsonElement? _members;

    // The key path from the owner to these values, as "exit." or "items[2].", and empty at the top.
    private readonly string _path;

    // One element of an authored array and its index, or an index of -1 for the members themselves.
    private readonly JsonElement _element;
    private readonly int _index;

    // Every key a read asked for, shared by each copy, or null when the members carry no key.
    private readonly List<string>? _asked;

    // An entry's members. References resolve against placed once every entry is constructed.
    internal AuthoredMembers(SceneDocumentEntry entry, int index, Dictionary<int, Entity>? placed = null, AssetCollection? assets = null)
        : this(new Owner(string.Create(CultureInfo.InvariantCulture, $"entities[{index}] ('{entry.Type}')"), "the entry", placed, assets), entry.Members, string.Empty)
    {
    }

    // The scene document's own members, read once every entry is constructed.
    internal AuthoredMembers(JsonElement? scene, Dictionary<int, Entity> placed, AssetCollection assets)
        : this(new Owner("the scene document", "the document", placed, assets), scene, string.Empty)
    {
    }

    private AuthoredMembers(Owner owner, JsonElement? members, string path)
    {
        _owner = owner;
        _members = members;
        _path = path;
        _index = -1;
        _asked = members is { ValueKind: JsonValueKind.Object } authored && authored.EnumerateObject().Any() ? [] : null;
        if (_asked is not null)
        {
            owner.Levels.Add(this);
        }
    }

    // One element of an authored array, read by the array's key.
    private AuthoredMembers(AuthoredMembers array, JsonElement element, int index)
    {
        this = array;
        _element = element;
        _index = index;
    }

    /// <summary>Whether the members author <paramref name="key"/>, as a JSON null included.</summary>
    public bool Has(string key) => Find(key, out _);

    /// <summary>Whether the members author <paramref name="key"/> as a JSON null.</summary>
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

    /// <summary>
    /// Reads a <see cref="Capsule.Rendering.Rect"/>, written <c>[left, top, right, bottom]</c> with all four finite
    /// and neither pair of edges crossed.
    /// </summary>
    public Rect Rect(string key) =>
        Authored(key) is { ValueKind: JsonValueKind.Array } value
        && value.GetArrayLength() == 4
        && TryFloat(value[0], out float left)
        && TryFloat(value[1], out float top)
        && TryFloat(value[2], out float right)
        && TryFloat(value[3], out float bottom)
        && right >= left
        && bottom >= top
            ? new Rect(left, top, right, bottom)
            : throw Mismatch(key, "Rect", "[left, top, right, bottom] with all four finite, right no less than left and bottom no less than top");

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

    /// <summary>Reads the enum member or definition the members name, which the applier then matches.</summary>
    public string Name(string key) =>
        Authored(key) is { ValueKind: JsonValueKind.String } value
            ? value.GetString()!
            : throw Mismatch(key, "a name", "a name in quotes");

    /// <summary>The failure for a name that matches none of <paramref name="names"/>.</summary>
    /// <param name="key">The member's key.</param>
    /// <param name="names">Every name the member accepts, comma-joined.</param>
    public SceneDocumentFormatException NotAName(string key, string names) =>
        new($"{Sets(key)} to {Found(Authored(key))}, which names nothing the member accepts. Write one of: {names}.");

    /// <summary>
    /// Reads a <c>[Flags]</c> enum's value, written as its members' names joined by commas, as the
    /// bits of each name ORed together. An empty string is no flags.
    /// </summary>
    /// <param name="key">The member's key.</param>
    /// <param name="member">The bits one camel-cased name stands for, or null for a name the enum lacks.</param>
    /// <param name="names">Every name the member accepts, comma-joined.</param>
    public ulong Flags(string key, Func<string, ulong?> member, string names)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (Authored(key) is not { ValueKind: JsonValueKind.String } value)
        {
            throw Mismatch(key, "flags", "member names in quotes, joined by commas");
        }

        string text = value.GetString()!;
        ulong flags = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return flags;
        }

        foreach (string name in text.Split(','))
        {
            flags |= member(name.Trim()) ?? throw new SceneDocumentFormatException(
                $"{Sets(key)} to {Found(value)}, whose \"{name.Trim()}\" names nothing the member accepts. Write one or more of: {names}, joined by commas.");
        }

        return flags;
    }

    /// <summary>
    /// Reads a texture, written as its key and extension as <c>"textures/hazard.png"</c> in any spelling
    /// that keys the same. The texture joins the scene's preload.
    /// </summary>
    /// <param name="key">The member's key.</param>
    /// <param name="find">The texture a normalized key and extension name, or null for one the game does not ship.</param>
    public TextureHandle Texture(string key, Func<string, TextureHandle?> find) =>
        Asset(
            key,
            find,
            AssetPaths.NormalizePath,
            "a texture's key and extension, as \"textures/hazard.png\"",
            "Write the key and extension of a texture under Assets/.",
            static (assets, texture) => assets.Add(texture));

    /// <summary>
    /// Reads a sound, written as its key and extension as <c>"audio/step.wav"</c> in any spelling that
    /// keys the same. The sound joins the scene's preload.
    /// </summary>
    /// <param name="key">The member's key.</param>
    /// <param name="find">The sound a normalized key and extension name, or null for one the game does not ship.</param>
    public AudioClip Sound(string key, Func<string, AudioClip?> find) =>
        Asset(
            key,
            find,
            AssetPaths.NormalizePath,
            "a sound's key and extension, as \"audio/step.wav\"",
            "Write the key and extension of a sound under Assets/.",
            static (assets, clip) => assets.Add(clip));

    /// <summary>
    /// Reads a scene document's key, written with no extension as <c>"scenes/halls/hall"</c> in any
    /// spelling that keys the same. The scene loads nothing until a request opens it.
    /// </summary>
    /// <param name="key">The member's key.</param>
    /// <param name="find">The scene a normalized key names, or null for one the game does not ship.</param>
    public SceneKey Scene(string key, Func<string, SceneKey?> find) =>
        Asset(
            key,
            find,
            static text => AssetPaths.NormalizeKey(text, out _),
            "a scene document's key, as \"scenes/halls/hall\"",
            "Write the key of a scene document under Assets/, with no extension.",
            join: null);

    /// <summary>
    /// Reads an array, written <c>[a, b, c]</c>, into a new array with one element read by
    /// <paramref name="element"/> per authored element.
    /// </summary>
    /// <param name="key">The member's key.</param>
    /// <param name="element">Reads one element from the value it is handed, by the same key.</param>
    public T[] Array<T>(string key, Func<AuthoredMembers, T> element)
    {
        ArgumentNullException.ThrowIfNull(element);
        JsonElement value = Authored(key);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Mismatch(key, typeof(T).Name + "[]", "an array, [a, b, c]");
        }

        T[] read = new T[value.GetArrayLength()];
        for (int i = 0; i < read.Length; i++)
        {
            read[i] = element(new AuthoredMembers(this, value[i], i));
        }

        return read;
    }

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
                $"{Sets(key)} to {Found(value)}, which {typeof(TConverter).Name} could not read as {typeof(T).Name}: {cause.Message} Write the value in the form that converter reads.",
                cause);
        }
    }

    /// <summary>
    /// Reads the entity an entry id names, which must be a <typeparamref name="T"/> placed by the same
    /// document. It is read once every entry is constructed.
    /// </summary>
    public T Entity<T>(string key)
        where T : class
    {
        JsonElement value = Authored(key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int id))
        {
            throw Mismatch(key, typeof(T).Name, "an entity id, a whole number");
        }

        if (_owner?.Placed is not { } placed || !placed.TryGetValue(id, out Entity? target))
        {
            throw new SceneDocumentFormatException(
                $"{Sets(key)} to {id}, which names no entity in the document. Write the id of an entity entry.");
        }

        return target as T ?? throw new SceneDocumentFormatException(
            $"{Sets(key)} to entity {id}, a {target.GetType().Name}, but the member takes {typeof(T).Name}. Write the id of an entity that is a {typeof(T).Name}.");
    }

    /// <summary>
    /// Reads an object, written <c>{ "member": value }</c>, as the values its class's authorable members read in turn.
    /// Its own <c>type</c> key names the class to construct.
    /// </summary>
    public AuthoredMembers Object(string key)
    {
        JsonElement value = Authored(key);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Mismatch(key, "an object", "{ \"member\": value }");
        }

        string path = _index < 0 ? $"{_path}{key}." : string.Create(CultureInfo.InvariantCulture, $"{_path}{key}[{_index}].");

        return new AuthoredMembers(_owner!, value, path);
    }

    /// <summary>The key of the class an object's <c>type</c> names, or null when it names none.</summary>
    public string? Type()
    {
        if (!Find(TypeKey, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new SceneDocumentFormatException($"{OwnerName} sets '{_path}{TypeKey}' to {Found(value)}. Write the key of a class in quotes.");
    }

    /// <summary>The failure for an object whose <c>type</c> names no class the member takes, or that needs a type and names none.</summary>
    /// <param name="types">Every key the member takes, comma-joined, or empty when it takes none.</param>
    public SceneDocumentFormatException NotAType(string types)
    {
        string member = _path.TrimEnd('.');
        string fix = types.Length == 0 ? "No class the member takes has a key." : $"Write one of: {types}.";

        return Type() is { } type
            ? new($"{OwnerName} sets '{_path}{TypeKey}' to \"{type}\", which names no class '{member}' takes. {fix}")
            : new($"{OwnerName} sets '{member}' with no type, and the member holds no object to fill. {fix}");
    }

    /// <summary>
    /// Defers <paramref name="set"/> until every entry of the document is constructed, for a member that reads an
    /// entity reference.
    /// </summary>
    /// <param name="target">The object whose member <paramref name="set"/> assigns.</param>
    /// <param name="set">Reads the reference off the members it is handed and assigns it.</param>
    public void Link(object target, Action<object, AuthoredMembers> set)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(set);
        _owner!.Links.Add((target, set, this));
    }

    // Sets every deferred reference, then throws when any level carries a key no read asked for. The keys asked
    // for are every authorable member the class declares, since the applier asks for each one.
    internal void Finish()
    {
        if (_owner is not { } owner)
        {
            return;
        }

        foreach ((object target, Action<object, AuthoredMembers> set, AuthoredMembers from) in owner.Links)
        {
            set(target, from);
        }

        owner.Links.Clear();
        foreach (AuthoredMembers level in owner.Levels)
        {
            level.RefuseUnread();
        }
    }

    private string OwnerName => _owner?.Name ?? "the spawn";

    // An entity constructor's refusal of what the entry authors, as the failure naming the entry.
    internal SceneDocumentFormatException Refused(Exception defect) =>
        new($"{OwnerName} cannot be built: {defect.Message}", defect);

    // The owner and what it sets: a key, or one element of the array a key holds.
    private string Sets(string key) =>
        _index < 0 ? $"{OwnerName} sets '{_path}{key}'" : string.Create(CultureInfo.InvariantCulture, $"{OwnerName} sets '{_path}{key}' element {_index}");

    private bool Find(string key, out JsonElement value)
    {
        value = default;
        if (_asked is { } asked && !asked.Contains(key))
        {
            asked.Add(key);
        }

        return _members is { } members && members.TryGetProperty(key, out value);
    }

    // The applier reads an optional member only after Has, so an absent key here is a required member.
    private JsonElement Authored(string key) =>
        _index >= 0 ? _element
        : Find(key, out JsonElement value)
            ? value
            : throw new SceneDocumentFormatException(
                $"{OwnerName} omits '{_path}{key}', which its class requires. Add \"{key}\" to {(_path.Length == 0 ? _owner?.Holder : $"'{_path.TrimEnd('.')}'")}.");

    private void RefuseUnread()
    {
        List<string> asked = _asked!;
        string path = _path;
        string[] unread = [.. _members!.Value.EnumerateObject().Select(static member => member.Name).Where(name => !asked.Contains(name))];
        if (unread.Length == 0)
        {
            return;
        }

        // A nested object takes a type only through a member it can be assigned to.
        if (_path.Length > 0 && unread.Contains(TypeKey))
        {
            throw new SceneDocumentFormatException(
                $"{OwnerName} sets '{_path}{TypeKey}', but '{_path.TrimEnd('.')}' fills the object its member holds and has no setter to take another. Drop the type, or give the member a setter.");
        }

        string declares = _asked!.Count == 0 ? "none" : string.Join(", ", _asked);

        throw new SceneDocumentFormatException(
            $"{OwnerName} sets {string.Join(", ", unread.Select(name => $"'{path}{name}'"))}, which no authorable member takes. "
            + $"{(_path.Length == 0 ? "Its" : $"'{_path.TrimEnd('.')}'s")} authorable members are: {declares}. Correct the key, or mark the member [Authorable].");
    }

    private SceneDocumentFormatException Mismatch(string key, string expected, string form)
    {
        JsonElement value = Authored(key);

        return new(value.ValueKind == JsonValueKind.Null && _index < 0
            ? $"{Sets(key)} to null, which only a nullable member accepts. The member takes {expected}. Write {form}, or make the member nullable."
            : $"{Sets(key)} to {Found(value)}, but the member takes {expected}. Write {form}.");
    }

    // An asset the members name by key: the string it wrote, normalized, resolved through find, and joined to the
    // scene's preload where join adds it.
    private T Asset<T>(string key, Func<string, T?> find, Func<string, string?> normalize, string form, string fix, Action<AssetCollection, T>? join)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(find);
        string authored = Authored(key) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : throw Mismatch(key, typeof(T).Name, form);
        string? keyed = normalize(authored);
        T asset = keyed is not null && find(keyed) is { } found
            ? found
            : throw new SceneDocumentFormatException($"{Sets(key)} to the string \"{authored}\", but no {typeof(T).Name} keys as \"{keyed ?? authored}\". {fix}");
        if (_owner?.Assets is { } assets)
        {
            join?.Invoke(assets, asset);
        }

        return asset;
    }

    private static bool TryFloat(JsonElement value, out float read)
    {
        read = value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) ? (float)number : float.NaN;

        return float.IsFinite(read);
    }

    // What the document wrote, short enough for one line of a message.
    private static string Found(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"the string \"{value.GetString()}\"",
        JsonValueKind.Number => string.Create(CultureInfo.InvariantCulture, $"the number {value.GetRawText()}"),
        JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ => "null",
    };

    // What every level of one owner's values shares: how a message names the owner and where a missing key goes,
    // what references resolve against, the preload, and every level and deferred reference read so far.
    private sealed class Owner(string name, string holder, Dictionary<int, Entity>? placed, AssetCollection? assets)
    {
        internal string Name => name;

        internal string Holder => holder;

        internal Dictionary<int, Entity>? Placed => placed;

        internal AssetCollection? Assets => assets;

        internal List<AuthoredMembers> Levels { get; } = [];

        internal List<(object Target, Action<object, AuthoredMembers> Set, AuthoredMembers From)> Links { get; } = [];
    }
}
