using Capsule.Assets;

namespace Capsule.Scenes.Documents;

/// <summary>The keys a scene document reserves for its own structure, grouped by the object that holds them.</summary>
/// <remarks>
/// Every other key of an object sets the <see cref="AuthorableAttribute"/> member of that name. An authorable
/// member that would take a reserved key is a compile error.
/// </remarks>
/// <example>
/// An importer refuses a custom property that would write an entry's own field a second time:
/// <code>
/// if (SceneDocumentKeys.Entry.Contains(name))
/// {
///     throw new FormatException($"'{name}' is a key the scene document reserves for the entry. Rename the property.");
/// }
/// </code>
/// </example>
public static class SceneDocumentKeys
{
    /// <summary>The keys of the document object itself.</summary>
    public static class Document
    {
        /// <summary>The URL of the format's JSON Schema, which gives an editor completion. The build ignores its value.</summary>
        public const string Schema = SchemaKeyConverter.Key;

        /// <summary>The key of the abstract <see cref="Scene"/> subclass the composed scene derives from.</summary>
        public const string BaseScene = "baseScene";

        /// <summary>Every entry the scene places, in composition order.</summary>
        public const string Entities = "entities";

        // Every key the document object reserves. The reader skips these when it collects members.
        internal static string[] Keys { get; } = [Schema, BaseScene, Entities];

        /// <summary>Whether the document object reserves <paramref name="key"/>, compared ordinally.</summary>
        public static bool Contains(string key) => Keys.AsSpan().Contains(key);
    }

    /// <summary>The keys of one entry in the document's <c>entities</c> list.</summary>
    public static class Entry
    {
        /// <summary>The type key of the entity class the entry places.</summary>
        public const string Type = "type";

        /// <summary>The positive id an entity reference names the entry by.</summary>
        public const string Id = "id";

        /// <summary>The placed x coordinate.</summary>
        public const string X = "x";

        /// <summary>The placed y coordinate.</summary>
        public const string Y = "y";

        /// <summary>The turn in degrees, clockwise on screen.</summary>
        public const string Rotation = "rotation";

        /// <summary>The scale factors, written <c>[x, y]</c>.</summary>
        public const string Scale = "scale";

        /// <summary>The draw band.</summary>
        public const string ZIndex = "zIndex";

        /// <summary>How far the entity moves with the camera, written <c>[x, y]</c>.</summary>
        public const string ScrollFactor = "scrollFactor";

        // Every key an entry reserves.
        internal static string[] Keys { get; } = [Type, Id, X, Y, Rotation, Scale, ZIndex, ScrollFactor];

        /// <summary>Whether an entry reserves <paramref name="key"/>, compared ordinally.</summary>
        public static bool Contains(string key) => Keys.AsSpan().Contains(key);
    }

    /// <summary>The keys of an object a member holds, at any depth below the document or an entry.</summary>
    public static class MemberObject
    {
        /// <summary>The type key of the subclass to construct in place of the member's own class.</summary>
        public const string Type = Entry.Type;

        /// <summary>Whether a member's object reserves <paramref name="key"/>, compared ordinally.</summary>
        public static bool Contains(string key) => key == Type;
    }
}
