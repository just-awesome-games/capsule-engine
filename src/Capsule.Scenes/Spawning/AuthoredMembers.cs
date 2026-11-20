using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Localization;
using Capsule.Navigation;
using Capsule.Rendering;

namespace Capsule.Scenes;

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
/// <para>
/// A scene document keeps what <see cref="Shared{T}"/> reads, with the assets its elements named, and the keys of
/// each object <see cref="Object(string)"/> visits outside an array. Every later composition of the same document
/// reuses them instead of reading its JSON again, and so does every other member of the document that authors the
/// same array. A repeated key reads its last value.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct AuthoredMembers
{
    private const string TypeKey = SceneDocumentKeys.MemberObject.Type;

    // Plain options with no reflection-based resolver, which a trimmed or ahead-of-time build keeps.
    private static readonly JsonSerializerOptions ConverterOptions = new();

    // What every owner of one composition shares, or null for a spawn built in code.
    private readonly Composition? _owner;

    private readonly AuthoredObject? _members;

    // The key path from the owner to these values, as "exit." or "items[2].", and empty at the top.
    private readonly string _path;

    // One element of an authored array and its index, or an index of -1 for the members themselves.
    private readonly ReadOnlyMemory<byte> _element;
    private readonly int _index;

    // The owner's index among the document's entries, or -1 for the document itself.
    private readonly int _entry;

    // Where the composition notes what every read at this level asked for, or -1 when the members carry no key. A
    // spawn built in code has no composition and reads no level.
    private readonly int _level;

    // An entry's members within composition, whose Finish reads them once every entry is constructed.
    internal AuthoredMembers(Composition composition, SceneDocumentEntry entry, int index)
        : this(composition, index, entry.Authored, string.Empty)
    {
    }

    // The scene document's own members, read once every entry is constructed.
    internal AuthoredMembers(SceneDocument scene, Dictionary<int, Entity> placed, AssetCollection assets)
        : this(new Composition(scene, placed, assets), -1, scene.Authored, string.Empty)
    {
    }

    private AuthoredMembers(Composition owner, int entry, AuthoredObject? members, string path)
    {
        _owner = owner;
        _members = members;
        _path = path;
        _index = -1;
        _entry = entry;
        _level = members is { Keys.Length: > 0 } ? owner.AddLevel(entry, members, path) : -1;
    }

    // One element of an authored array, read by the array's key.
    private AuthoredMembers(AuthoredMembers array, ReadOnlyMemory<byte> element, int index)
    {
        this = array;
        _element = element;
        _index = index;
    }

    /// <summary>Whether the members author <paramref name="key"/>, as a JSON null included.</summary>
    public bool Has(string key) => Find(key, out _);

    /// <summary>Whether the members author <paramref name="key"/> as a JSON null.</summary>
    public bool IsNull(string key) => Find(key, out ReadOnlyMemory<byte> value) && AuthoredValue.Kind(value.Span) == JsonValueKind.Null;

    /// <summary>Reads a <see langword="bool"/>, written <c>true</c> or <c>false</c>.</summary>
    public bool Bool(string key) => AuthoredValue.Kind(Authored(key).Span) switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Mismatch(key, "bool", "true or false"),
    };

    /// <summary>Reads an <see langword="int"/>, written as a whole number in range.</summary>
    public int Int(string key) =>
        AuthoredValue.TryInt(Authored(key).Span, out int read)
            ? read
            : throw Mismatch(key, "int", "a whole number from -2147483648 to 2147483647");

    /// <summary>Reads a <see langword="float"/>, written as a finite number.</summary>
    public float Float(string key) =>
        AuthoredValue.TryFloat(Authored(key).Span, out float read) ? read : throw Mismatch(key, "float", "a finite number");

    /// <summary>Reads a <see langword="string"/>.</summary>
    public string String(string key) =>
        AuthoredValue.String(Authored(key).Span) ?? throw Mismatch(key, "string", "a string in quotes");

    /// <summary>Reads a <see cref="System.Numerics.Vector2"/>, written <c>[x, y]</c> with both finite.</summary>
    public Vector2 Vector2(string key)
    {
        Span<float> read = stackalloc float[2];

        return AuthoredValue.TryFloats(Authored(key), read)
            ? new Vector2(read[0], read[1])
            : throw Mismatch(key, "Vector2", "[x, y] with both numbers finite");
    }

    /// <summary>
    /// Reads a <see cref="Capsule.Rendering.Rect"/>, written <c>[left, top, right, bottom]</c> with all four finite
    /// and neither pair of edges crossed.
    /// </summary>
    public Rect Rect(string key)
    {
        Span<float> read = stackalloc float[4];

        return AuthoredValue.TryFloats(Authored(key), read)
            && read[2] >= read[0]
            && read[3] >= read[1]
            ? Capsule.Rendering.Rect.FromEdges(read[0], read[1], read[2], read[3])
            : throw Mismatch(key, "Rect", "[left, top, right, bottom] with all four finite, right no less than left and bottom no less than top");
    }

    /// <summary>
    /// Reads a <see cref="Path2D"/>, written as its points <c>[[x, y], [x, y]]</c>, at least two of them, all finite
    /// and not all the same.
    /// </summary>
    public Path2D Path(string key)
    {
        if (AuthoredValue.Kind(Authored(key).Span) != JsonValueKind.Array)
        {
            throw Mismatch(key, "Path2D", "its points, [[x, y], [x, y]]");
        }

        try
        {
            return new Path2D(Array(key, element => element.Vector2(key)));
        }
        catch (ArgumentException ex)
        {
            throw new SceneDocumentFormatException($"{Sets(key)} to points that make no path. {ex.Message}", ex);
        }
    }

    /// <summary>Reads a <see cref="ColorRgba"/>, written <c>"#rrggbb"</c> or <c>"#rrggbbaa"</c>.</summary>
    public ColorRgba Color(string key) =>
        AuthoredValue.TryColor(Authored(key).Span, out ColorRgba read)
            ? read
            : throw Mismatch(key, "ColorRgba", "\"#rrggbb\" or \"#rrggbbaa\"");

    /// <summary>Reads the enum member or definition the members name, which the applier then matches.</summary>
    public string Name(string key) =>
        AuthoredValue.String(Authored(key).Span) ?? throw Mismatch(key, "a name", "a name in quotes");

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
        ReadOnlyMemory<byte> value = Authored(key);
        if (AuthoredValue.String(value.Span) is not { } text)
        {
            throw Mismatch(key, "flags", "member names in quotes, joined by commas");
        }

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

    /// <summary>Reads a text, written as its key path exactly as its source language authors it, as <c>"hints.jump"</c>.</summary>
    /// <param name="key">The member's key.</param>
    /// <param name="find">The text a key path names, or null for one the game does not ship.</param>
    public TextKey Text(string key, Func<string, TextKey?> find) =>
        Asset(
            key,
            find,
            static text => text,
            "a text's key path, as \"hints.jump\"",
            "Write the key path of a text in the source language's .strings.json files.",
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
        ReadOnlyMemory<byte> value = Authored(key);
        if (AuthoredValue.Kind(value.Span) != JsonValueKind.Array)
        {
            throw Mismatch(key, typeof(T).Name + "[]", "an array, [a, b, c]");
        }

        T[] read = new T[AuthoredValue.Count(value)];
        AuthoredValue.Elements elements = new(value);
        for (int i = 0; elements.Next(out ReadOnlyMemory<byte> item); i++)
        {
            read[i] = element(new AuthoredMembers(this, item, i));
        }

        return read;
    }

    /// <summary>
    /// Reads an array, written <c>[a, b, c]</c>, once per scene document. Every scene composed from the document
    /// receives the same elements, and an object among them is one instance they all hold.
    /// </summary>
    /// <remarks>
    /// Members of one document that author the same array, byte for byte, and read it through the same
    /// <paramref name="element"/> receive the same elements too. An asset an element names joins the preload of
    /// every scene composed from the document. A key no read asked for at any level of an element fails this read.
    /// <paramref name="element"/> must read no entity reference, which goes through <see cref="Array{T}"/>.
    /// </remarks>
    /// <param name="key">The member's key.</param>
    /// <param name="element">Reads one element from the value it is handed, by the same key.</param>
    public ReadOnlyMemory<T> Shared<T>(string key, Func<AuthoredMembers, T> element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ReadOnlyMemory<byte> array = Authored(key);
        if (_index >= 0 || _owner is not { } owner)
        {
            return Array(key, element);
        }

        if (_members!.Kept(key) is not SharedRead<T> read)
        {
            if (owner.Shared.Kept(array, element) is not SharedRead<T> same)
            {
                same = (SharedRead<T>)owner.Shared.Keep(array, element, ReadShared(owner, key, element));
            }

            read = (SharedRead<T>)_members.Keep(key, same);
        }

        (owner.Sharing ?? owner.Assets).Add(read.Assets);

        return read.Elements;
    }

    /// <summary>
    /// Reads a value through <typeparamref name="TConverter"/>, the converter the member's type names with
    /// <see cref="JsonConverterAttribute"/>. A JSON null is refused.
    /// </summary>
    public T Read<T, TConverter>(string key)
        where TConverter : JsonConverter<T>, new()
    {
        ReadOnlyMemory<byte> value = Authored(key);
        if (AuthoredValue.Kind(value.Span) == JsonValueKind.Null)
        {
            throw Mismatch(key, typeof(T).Name, "the form " + typeof(TConverter).Name + " reads");
        }

        try
        {
            Utf8JsonReader reader = new(value.Span);
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
        if (!AuthoredValue.TryInt(Authored(key).Span, out int id))
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
        ReadOnlyMemory<byte> value = Authored(key);
        if (AuthoredValue.Kind(value.Span) != JsonValueKind.Object)
        {
            throw Mismatch(key, "an object", "{ \"member\": value }");
        }

        if (_index >= 0)
        {
            return new AuthoredMembers(_owner!, _entry, AuthoredObject.Parse(value, _owner!.Keys), string.Create(CultureInfo.InvariantCulture, $"{_path}{key}[{_index}]."));
        }

        if (_members!.Kept(key) is not AuthoredObject nested)
        {
            nested = (AuthoredObject)_members.Keep(key, AuthoredObject.Parse(value, _owner!.Keys));
        }

        return new AuthoredMembers(_owner!, _entry, nested, $"{_path}{key}.");
    }

    /// <summary>The key of the class an object's <c>type</c> names, or null when it names none.</summary>
    public string? Type()
    {
        if (!Find(TypeKey, out ReadOnlyMemory<byte> value))
        {
            return null;
        }

        return AuthoredValue.String(value.Span) ?? throw new SceneDocumentFormatException($"{OwnerName} sets '{_path}{TypeKey}' to {Found(value)}. Write the key of a class in quotes.");
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
        _owner!.Defer(target, set, this);
    }

    // Finishes the composition these members belong to.
    internal void Finish() => _owner?.Finish();

    private string OwnerName => _owner?.Name(_entry) ?? "the spawn";

    // Where a missing key of the owner's own values goes.
    private string? Holder => _owner is null ? null : _entry < 0 ? "the document" : "the entry";

    // Whether these are an owner's own members, not a nested object's or an array element's.
    private bool IsOwn => _members is not null && _index < 0 && _path.Length == 0;

    // What build made of key's value, kept with the document so every composition of it shares one, or null when
    // the members author no key. Only an entry's own members, not an array element, keep a read.
    internal T? Built<T>(string key, Func<ReadOnlyMemory<byte>, T> build)
        where T : class
    {
        if (_index >= 0 || !Find(key, out ReadOnlyMemory<byte> value))
        {
            return null;
        }

        return _members!.Kept(key) as T ?? (T)_members.Keep(key, build(value));
    }

    // What the owner's class built from all of its members and kept with the document, or null when no composition
    // has. Only an owner's own members keep one.
    internal T? Whole<T>()
        where T : class => IsOwn ? _members!.Whole as T : null;

    // Keeps built with the document unless a racing composition kept one first, and returns the one every
    // composition of the document then shares. Members with nothing authored keep nothing.
    internal T KeepWhole<T>(T built)
        where T : class => IsOwn ? (T)_members!.KeepWhole(built) : built;

    // An entity constructor's refusal of what the entry authors, as the failure naming the entry.
    internal SceneDocumentFormatException Refused(Exception defect) =>
        new($"{OwnerName} cannot be built: {defect.Message}", defect);

    // Reads key's array for every scene and every member that shares it. The elements' assets are gathered apart
    // from this scene's, and their levels are checked here, since no later read visits them again.
    private SharedRead<T> ReadShared<T>(Composition owner, string key, Func<AuthoredMembers, T> element)
    {
        AssetCollection? outer = owner.Sharing;
        AssetCollection named = new();
        int levels = owner.LevelCount;
        owner.Sharing = named;
        try
        {
            T[] elements = Array(key, element);
            owner.RefuseLevelsFrom(levels);

            return new SharedRead<T>(elements, named);
        }
        finally
        {
            owner.Sharing = outer;
        }
    }

    // The owner and what it sets: a key, or one element of the array a key holds.
    private string Sets(string key) =>
        _index < 0 ? $"{OwnerName} sets '{_path}{key}'" : string.Create(CultureInfo.InvariantCulture, $"{OwnerName} sets '{_path}{key}' element {_index}");

    private bool Find(string key, out ReadOnlyMemory<byte> value)
    {
        int index = _members?.IndexOf(key) ?? -1;
        if (_level >= 0)
        {
            _owner?.Ask(_level, key, index);
        }

        value = index >= 0 ? _members!.ValueAt(index) : default;

        return index >= 0;
    }

    // The applier reads an optional member only after Has. An absent key here is a required member.
    private ReadOnlyMemory<byte> Authored(string key) =>
        _index >= 0 ? _element
        : Find(key, out ReadOnlyMemory<byte> value)
            ? value
            : throw new SceneDocumentFormatException(
                $"{OwnerName} omits '{_path}{key}', which its class requires. Add \"{key}\" to {(_path.Length == 0 ? Holder : $"'{_path.TrimEnd('.')}'")}.");

    private SceneDocumentFormatException Mismatch(string key, string expected, string form)
    {
        ReadOnlyMemory<byte> value = Authored(key);

        return new(AuthoredValue.Kind(value.Span) == JsonValueKind.Null && _index < 0
            ? $"{Sets(key)} to null, which only a nullable member accepts. The member takes {expected}. Write {form}, or make the member nullable."
            : $"{Sets(key)} to {Found(value)}, but the member takes {expected}. Write {form}.");
    }

    // An asset the members name by key: the string it wrote, normalized, resolved through find, and joined to the
    // scene's preload where join adds it.
    private T Asset<T>(string key, Func<string, T?> find, Func<string, string?> normalize, string form, string fix, Action<AssetCollection, T>? join)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(find);
        string authored = AuthoredValue.String(Authored(key).Span) ?? throw Mismatch(key, typeof(T).Name, form);
        string? keyed = normalize(authored);
        T asset = keyed is not null && find(keyed) is { } found
            ? found
            : throw new SceneDocumentFormatException($"{Sets(key)} to the string \"{authored}\", but no {typeof(T).Name} keys as \"{keyed ?? authored}\". {fix}");
        if ((_owner?.Sharing ?? _owner?.Assets) is { } assets)
        {
            join?.Invoke(assets, asset);
        }

        return asset;
    }

    // What the document wrote, short enough for one line of a message.
    private static string Found(ReadOnlyMemory<byte> value) => AuthoredValue.Kind(value.Span) switch
    {
        JsonValueKind.String => $"the string \"{AuthoredValue.String(value.Span)}\"",
        JsonValueKind.Number => $"the number {AuthoredValue.Text(value.Span)}",
        JsonValueKind.True or JsonValueKind.False => AuthoredValue.Text(value.Span),
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ => "null",
    };

    // What one shared read made of a key's array: the elements, and every asset they named.
    private sealed record SharedRead<T>(T[] Elements, AssetCollection Assets);

    // A deferred reference: the object whose member Set assigns, and the values it reads the reference from.
    private readonly record struct Deferred(object Target, Action<object, AuthoredMembers> Set, AuthoredMembers From);

    // One level of an owner's values that authors keys: which of its own keys a read asked for, and every key asked,
    // each once in the order first asked. A key the level authors is marked by its place among the level's keys, and
    // a repeated read of it costs no search. The composition keeps the level to refuse a key no read asked for.
    private struct Level(int entry, AuthoredObject members, string path, AskedKey none)
    {
        internal readonly int Entry = entry;

        // Bit i marks the level's key i as read: the first 64 here, the rest in _beyond.
        private readonly ulong[]? _beyond = members.Keys.Length > 64 ? new ulong[(members.Keys.Length - 1) >> 6] : null;
        private ulong _read;

        // The last key asked for, which leads back through every key asked before it.
        private AskedKey _asked = none;

        // Notes key asked for, where index is its place among the level's keys or -1 when the level does not author it.
        internal void Add(string key, int index)
        {
            if (index >= 0 ? Mark(index) : !_asked.Holds(key))
            {
                _asked = _asked.Then(key);
            }
        }

        // Throws when a key the level authors was never asked for, naming every key that was.
        internal void RefuseUnread(Composition owner)
        {
            string[] keys = members.Keys;

            // A valid document reads every key, which this checks without allocating.
            int read = 0;
            while (read < keys.Length && IsRead(read))
            {
                read++;
            }

            if (read == keys.Length)
            {
                return;
            }

            List<string> unread = [];
            bool typed = false;
            for (int index = read; index < keys.Length; index++)
            {
                if (!IsRead(index))
                {
                    unread.Add($"'{path}{keys[index]}'");
                    typed |= keys[index] == TypeKey;
                }
            }

            // A nested object takes a type only through a member it can be assigned to.
            if (typed && path.Length > 0)
            {
                throw new SceneDocumentFormatException(
                    $"{owner.Name(Entry)} sets '{path}{TypeKey}', but '{path.TrimEnd('.')}' fills the object its member holds and has no setter to take another. Drop the type, or give the member a setter.");
            }

            string declares = _asked.Key is null ? "none" : string.Join(", ", _asked.Keys());

            throw new SceneDocumentFormatException(
                $"{owner.Name(Entry)} sets {string.Join(", ", unread)}, which no authorable member takes. "
                + $"{(path.Length == 0 ? "Its" : $"'{path.TrimEnd('.')}'s")} authorable members are: {declares}. Correct the key, or mark the member [Authorable].");
        }

        private bool IsRead(int index) => (Word(index) & (1UL << index)) != 0;

        // Marks the key at index read, and returns false when it already was.
        private bool Mark(int index)
        {
            ref ulong word = ref Word(index);
            ulong bit = 1UL << index;
            bool unread = (word & bit) == 0;
            word |= bit;

            return unread;
        }

        // The word holding key index's bit. A shift takes its count modulo 64, which is the key's bit within it.
        [UnscopedRef]
        private ref ulong Word(int index) => ref index < 64 ? ref _read : ref _beyond![(index >> 6) - 1];
    }

    // A key a level asked for after the keys of the node before it, where the composition's first node stands for
    // none. Levels that ask the same keys in the same order share every node. The many entries of one class note
    // their keys once between them.
    private sealed class AskedKey(string? key, AskedKey? previous)
    {
        internal readonly string? Key = key;
        private readonly AskedKey? _previous = previous;

        // The first key asked after this one, and the next key asked after the node before this one in its place.
        private AskedKey? _then;
        private AskedKey? _sibling;

        // The node for key asked after this one.
        internal AskedKey Then(string asked)
        {
            for (AskedKey? next = _then; next is not null; next = next._sibling)
            {
                if (next.Key == asked)
                {
                    return next;
                }
            }

            return _then = new AskedKey(asked, this) { _sibling = _then };
        }

        // Whether this node or one before it asked for asked.
        internal bool Holds(string asked)
        {
            for (AskedKey? node = this; node?.Key is { } held; node = node._previous)
            {
                if (held == asked)
                {
                    return true;
                }
            }

            return false;
        }

        // Every key asked through this node, in the order asked.
        internal List<string> Keys()
        {
            List<string> keys = [];
            for (AskedKey? node = this; node?.Key is { } held; node = node._previous)
            {
                keys.Add(held);
            }

            keys.Reverse();
            return keys;
        }
    }

    // A list that grows a block at a time and never copies what it holds once past its first block, which a large
    // composition fills with no doubled array.
    private sealed class Table<T>
    {
        private const int BlockBits = 8;
        private const int Block = 1 << BlockBits;

        // Every block holds Block items but the first, which doubles from a few up to Block.
        private readonly List<T[]> _blocks = [];

        internal int Count { get; private set; }

        internal ref T this[int index] => ref _blocks[index >> BlockBits][index & (Block - 1)];

        // Appends item and returns its index.
        internal int Add(in T item)
        {
            int block = Count >> BlockBits;
            if (block == _blocks.Count)
            {
                _blocks.Add(new T[block == 0 ? 4 : Block]);
            }
            else if (block == 0 && Count == _blocks[0].Length)
            {
                T[] first = _blocks[0];
                System.Array.Resize(ref first, Count * 2);
                _blocks[0] = first;
            }

            this[Count] = item;

            return Count++;
        }
    }

    // What every owner of one composition shares: how a message names an owner, what references resolve against, the
    // preload, the document's shared reads, and every level and deferred reference read so far. Finish reads the
    // levels and references once every owner is constructed.
    internal sealed class Composition
    {
        // The document whose entries own the values.
        private readonly SceneDocument _document;

        private readonly AskedKey _none = new(null, null);
        private readonly Table<Level> _levels = new();
        private readonly Table<Deferred> _links = new();

        // Where an asset joins in place of Assets while a shared read runs, and null outside one.
        internal AssetCollection? Sharing { get; set; }

        // The names of the objects read off the owners' values, which the many elements of one array share.
        internal AuthoredObject.KeyTable Keys => field ??= new();

        // The document's shared reads.
        internal SharedReads Shared { get; }

        internal Dictionary<int, Entity> Placed { get; }

        internal AssetCollection Assets { get; }

        // The document's entries, or its own members. References resolve against placed.
        internal Composition(SceneDocument document, Dictionary<int, Entity> placed, AssetCollection assets)
        {
            _document = document;
            Placed = placed;
            Assets = assets;
            Shared = document.Shared;
        }

        internal int LevelCount => _levels.Count;

        // How a message names the owner of entry's values, where an entry of -1 is the document itself.
        internal string Name(int entry) =>
            entry < 0 ? "the scene document" : string.Create(CultureInfo.InvariantCulture, $"entities[{entry}] ('{_document.Entries[entry].Type}')");

        internal int AddLevel(int entry, AuthoredObject members, string path) => _levels.Add(new Level(entry, members, path, _none));

        internal void Ask(int level, string key, int index) => _levels[level].Add(key, index);

        internal void Defer(object target, Action<object, AuthoredMembers> set, AuthoredMembers from) => _links.Add(new Deferred(target, set, from));

        // Refuses every level from first on, which no later read visits. Finish checks them again and finds nothing new.
        internal void RefuseLevelsFrom(int first)
        {
            for (int index = first; index < _levels.Count; index++)
            {
                _levels[index].RefuseUnread(this);
            }
        }

        // Sets every deferred reference, then throws when any level carries a key no read asked for, one owner at a
        // time in document order. The keys asked for are every authorable member the class declares, since the
        // applier asks for each one.
        internal void Finish()
        {
            int link = 0;
            int level = 0;
            while (link < _links.Count || level < _levels.Count)
            {
                int entry = Math.Min(
                    link < _links.Count ? _links[link].From._entry : int.MaxValue,
                    level < _levels.Count ? _levels[level].Entry : int.MaxValue);
                for (; link < _links.Count && _links[link].From._entry == entry; link++)
                {
                    Deferred next = _links[link];
                    next.Set(next.Target, next.From);
                }

                for (; level < _levels.Count && _levels[level].Entry == entry; level++)
                {
                    _levels[level].RefuseUnread(this);
                }
            }
        }
    }
}
