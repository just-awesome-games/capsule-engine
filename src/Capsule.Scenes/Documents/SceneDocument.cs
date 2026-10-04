using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Capsule.Assets;

namespace Capsule.Scenes.Documents;

/// <summary>A scene as data: its ordered entries and the members it authors, each as raw JSON its class reads.</summary>
/// <remarks>
/// File order is composition order. The constructor enforces the format's invariants, and every
/// document that exists is valid.
/// </remarks>
public sealed class SceneDocument
{
    private const string KeyForm =
        "A key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name.";

    private static readonly string[] RootKeys = ["$schema", "baseScene", "entities"];
    private static readonly string[] EntryKeys = ["id", "type", "x", "y", "rotation", "scale", "zIndex", "scrollFactor"];

    private readonly SceneDocumentEntry[] _entries;

    /// <summary>A validated document over <paramref name="entries"/>.</summary>
    /// <param name="entries">Every entry, in composition order.</param>
    /// <param name="properties">
    /// The document's other top-level keys as one JSON object, or null when it authors none. Each sets the
    /// authorable member of that name on the composed scene.
    /// </param>
    /// <param name="baseScene">The key of the abstract <see cref="Scene"/> subclass the composed scene derives from, or null.</param>
    /// <exception cref="ArgumentException">The document is malformed. The message names the defect.</exception>
    /// <example>
    /// An importer for another editor's format builds a document whose scene member names the entry with id 3:
    /// <code>
    /// JsonElement properties = JsonSerializer.SerializeToElement(new { startBounds = 3 });
    /// SceneDocument document = new([new SceneDocumentEntry("camera-bounds") { Id = 3 }], properties);
    /// </code>
    /// </example>
    public SceneDocument(IReadOnlyList<SceneDocumentEntry> entries, JsonElement? properties = null, string? baseScene = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = [.. entries];
        Properties = properties;
        BaseScene = baseScene;

        ValidateRoot();
        ValidateEntries();
    }

    /// <summary>Every entry, in composition order.</summary>
    public ReadOnlySpan<SceneDocumentEntry> Entries => _entries;

    /// <summary>The document's other top-level keys as one JSON object, or null when it authors none.</summary>
    public JsonElement? Properties { get; }

    /// <summary>The key of the abstract <see cref="Scene"/> subclass the composed scene derives from, or null.</summary>
    public string? BaseScene { get; }

    // How a message names the entry at index: its place in the list and what it places.
    private static string Describe(SceneDocumentEntry entry, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"entities[{index}] ('{entry.Type}' at ({entry.X}, {entry.Y}))");

    private void ValidateEntries()
    {
        HashSet<int> seen = [];
        for (int i = 0; i < _entries.Length; i++)
        {
            SceneDocumentEntry entry = _entries[i];
            if (string.IsNullOrWhiteSpace(entry.Type))
            {
                throw Malformed($"entities[{i}] has no type. Name the spawn type it composes.");
            }

            string named = Describe(entry, i);
            if (entry.Id is < 1)
            {
                throw Malformed(string.Create(CultureInfo.InvariantCulture, $"{named} has id {entry.Id}. Make it positive, or omit it."));
            }

            if (entry.Id is { } id && !seen.Add(id))
            {
                throw Malformed(string.Create(CultureInfo.InvariantCulture, $"{named} has id {id}, which an earlier entry has. Give every entry a unique id."));
            }

            // NaN and the infinities have no JSON number, so such a document could not be written out.
            Vector2 factor = entry.ScrollFactor ?? Vector2.One;
            if (!float.IsFinite(entry.X + entry.Y + entry.RotationDegrees + factor.X + factor.Y))
            {
                throw Malformed($"{named} places with a number that is not finite. Make its position, rotation and scroll factor finite.");
            }

            // A scale of zero or less has no size, and a non-finite one has no JSON number.
            if (!IsScale(entry.ScaleX) || !IsScale(entry.ScaleY))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{named} is scaled ({entry.ScaleX}, {entry.ScaleY}), which is not a scale. Make both factors finite and greater than zero."));
            }

            // What each key means is the claiming class's contract, checked when the entry spawns.
            if (entry.Properties is { ValueKind: not JsonValueKind.Object })
            {
                throw Malformed($"{named} has properties that are not an object. Write them as {{ \"name\": value }}, or omit them.");
            }

            if (Reserved(entry.Properties, EntryKeys) is { } entryKey)
            {
                throw Malformed($"{named} has a member '{entryKey}', which the format reserves for the entry itself. Set it on the entry.");
            }
        }
    }

    private void ValidateRoot()
    {
        if (BaseScene is { } baseScene && !AssetPaths.IsKey(baseScene))
        {
            throw Malformed($"baseScene is '{baseScene}', which is not a key. {KeyForm}", "baseScene");
        }

        if (Properties is { ValueKind: not JsonValueKind.Object })
        {
            throw Malformed("the scene's properties are not an object. Write them as { \"name\": value }, or omit them.", "properties");
        }

        if (Reserved(Properties, RootKeys) is { } rootKey)
        {
            throw Malformed($"the scene's properties have a member '{rootKey}', which the format reserves for the document itself.", "properties");
        }
    }

    // The first of keys the object authors, which the writer would write a second time.
    private static string? Reserved(JsonElement? properties, string[] keys) =>
        properties is { ValueKind: JsonValueKind.Object } members
            ? members.EnumerateObject().Select(static member => member.Name).FirstOrDefault(keys.Contains)
            : null;

    private static bool IsScale(float factor) => float.IsFinite(factor) && factor > 0f;

    private static ArgumentException Malformed(string message, string parameterName = "entries") =>
        new(message, parameterName);
}
