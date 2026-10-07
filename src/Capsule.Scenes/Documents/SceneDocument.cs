using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes.Documents;

/// <summary>A scene as data: its ordered entries and the members it authors, each as raw JSON its class reads.</summary>
/// <remarks>
/// File order is composition order. The constructor enforces the format's invariants, and every
/// document that exists is valid. <see cref="Parse"/> reads the JSON form and <see cref="ToJson"/> writes it.
/// </remarks>
public sealed class SceneDocument
{
    private const string KeyForm =
        "A key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name.";

    private readonly SceneDocumentEntry[] _entries;

    /// <summary>The key of the abstract <see cref="Scene"/> subclass the composed scene derives from, or null.</summary>
    public string? BaseScene { get; }

    // The document's members, or null when it authors none.
    internal AuthoredObject? Authored { get; }

    /// <summary>A validated document over <paramref name="entries"/>.</summary>
    /// <param name="entries">Every entry, in composition order.</param>
    /// <param name="members">
    /// The document's other top-level keys as one JSON object, or null when it authors none. Each sets the
    /// authorable member of that name on the composed scene.
    /// </param>
    /// <param name="baseScene">The key of the abstract <see cref="Scene"/> subclass the composed scene derives from, or null.</param>
    /// <exception cref="ArgumentException">The document is malformed. The message names the defect.</exception>
    /// <example>
    /// An importer for another editor's format builds a document whose scene member names the entry with id 3:
    /// <code>
    /// JsonElement members = JsonSerializer.SerializeToElement(new { startBounds = 3 });
    /// SceneDocument document = new([new SceneDocumentEntry("camera-bounds") { Id = 3 }], members);
    /// </code>
    /// </example>
    public SceneDocument(IReadOnlyList<SceneDocumentEntry> entries, JsonElement? members = null, string? baseScene = null)
        : this([.. entries ?? throw new ArgumentNullException(nameof(entries))], ObjectOf(members), baseScene)
    {
    }

    // A document over entries, which it keeps.
    internal SceneDocument(SceneDocumentEntry[] entries, AuthoredObject? members, string? baseScene)
    {
        _entries = entries;
        Authored = members;
        BaseScene = baseScene;

        ValidateRoot();
        ValidateEntries();
    }

    /// <summary>Every entry, in composition order.</summary>
    public ReadOnlySpan<SceneDocumentEntry> Entries => _entries;

    /// <summary>The document's other top-level keys as one JSON object, or null when it authors none.</summary>
    /// <remarks>Each read builds a new object, which holds every key once.</remarks>
    public JsonElement? Members => Authored?.ToElement();

    /// <summary>Reads a document from its JSON form.</summary>
    /// <exception cref="SceneDocumentFormatException">The JSON is malformed or the document breaks the format.</exception>
    /// <example>
    /// <code>
    /// SceneDocument document = SceneDocument.Parse(File.ReadAllText("room.scene.json"));
    /// </code>
    /// </example>
    public static SceneDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return ParseUtf8(Encoding.UTF8.GetBytes(json));
    }

    // Reads a document from its JSON form as UTF-8. The document keeps utf8 as its members' bytes. The caller
    // never changes them afterwards.
    internal static SceneDocument ParseUtf8(byte[] utf8)
    {
        try
        {
            return SceneDocumentJson.Read(utf8);
        }
        catch (ArgumentException ex)
        {
            // The document model reports a defect as a bad argument. Coming from a file, the same
            // defect is a malformed document.
            throw new SceneDocumentFormatException(ex.Message, ex);
        }
    }

    /// <summary>Writes the document as compact JSON, which <see cref="Parse"/> reads back to the same entries and members.</summary>
    /// <remarks>An importer writes the document it builds with this. The same document always writes the same text.</remarks>
    public string ToJson() => SceneDocumentJson.Write(this);

    // How a message names the entry at index: its place in the list and what it places.
    private static string Describe(SceneDocumentEntry entry, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"entities[{index}] ('{entry.Type}' at ({entry.Spawn.Position.X}, {entry.Spawn.Position.Y}))");

    private void ValidateEntries()
    {
        HashSet<int> seen = [];
        for (int i = 0; i < _entries.Length; i++)
        {
            SceneDocumentEntry entry = _entries[i];
            if (string.IsNullOrWhiteSpace(entry.Type))
            {
                throw Malformed($"entities[{i}] has no type. Name the type key of the entity class it places.");
            }

            if (entry.Id is < 1)
            {
                throw Malformed(string.Create(CultureInfo.InvariantCulture, $"{Describe(entry, i)} has id {entry.Id}. Make it positive, or omit it."));
            }

            if (entry.Id is { } id && !seen.Add(id))
            {
                throw Malformed(string.Create(CultureInfo.InvariantCulture, $"{Describe(entry, i)} has id {id}, which an earlier entry has. Give every entry a unique id."));
            }

            // NaN and the infinities have no JSON number, so such a document could not be written out.
            EntitySpawn spawn = entry.Spawn;
            Vector2 factor = spawn.ScrollFactor ?? Vector2.One;
            if (!float.IsFinite(spawn.Position.X + spawn.Position.Y + spawn.Rotation + factor.X + factor.Y))
            {
                throw Malformed($"{Describe(entry, i)} places with a number that is not finite. Make its position, rotation and scroll factor finite.");
            }

            // A scale of zero or less has no size, and a non-finite one has no JSON number.
            if (!IsScale(spawn.Scale.X) || !IsScale(spawn.Scale.Y))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(entry, i)} is scaled ({spawn.Scale.X}, {spawn.Scale.Y}), which is not a scale. Make both factors finite and greater than zero."));
            }

            // What each key means is the claiming class's contract, checked when the entry spawns.
            if (entry.NotAnObject is not null)
            {
                throw Malformed($"{Describe(entry, i)} has members that are not an object. Write them as {{ \"name\": value }}, or omit them.");
            }

            if (Array.Find(entry.Authored?.Keys ?? [], SceneDocumentKeys.Entry.Contains) is { } entryKey)
            {
                throw Malformed($"{Describe(entry, i)} has a member '{entryKey}', which the format reserves for the entry itself. Set it on the entry.");
            }
        }
    }

    private void ValidateRoot()
    {
        if (BaseScene is { } baseScene && !AssetPaths.IsKey(baseScene))
        {
            throw Malformed($"baseScene is '{baseScene}', which is not a key. {KeyForm}", "baseScene");
        }

        if (Array.Find(Authored?.Keys ?? [], SceneDocumentKeys.Document.Contains) is { } rootKey)
        {
            throw Malformed($"the scene's members include '{rootKey}', which the format reserves for the document itself.", "members");
        }
    }

    // The members an importer handed in.
    private static AuthoredObject? ObjectOf(JsonElement? members) => members switch
    {
        null => null,
        { ValueKind: JsonValueKind.Object } authored => AuthoredObject.From(authored),
        _ => throw Malformed("the scene's members are not an object. Write them as { \"name\": value }, or omit them.", "members"),
    };

    private static bool IsScale(float factor) => float.IsFinite(factor) && factor > 0f;

    private static ArgumentException Malformed(string message, string parameterName = "entries") =>
        new(message, parameterName);
}
