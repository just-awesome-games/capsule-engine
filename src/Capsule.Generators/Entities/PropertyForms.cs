namespace Capsule.Generators;

/// <summary>A type a scene document writes in a JSON form of the engine's own.</summary>
/// <param name="Type">The type as the generator displays it fully qualified.</param>
/// <param name="Read">The <c>AuthoredProperties</c> method the spawner reads it with.</param>
internal sealed record BuiltInForm(string Type, string Read);

/// <summary>An asset type a scene document names by key, resolved at load through a generated lookup.</summary>
/// <param name="Type">The type as the generator displays it fully qualified.</param>
/// <param name="Read">The <c>AuthoredProperties</c> method the spawner reads it with, which also names its lookup.</param>
/// <param name="Scene">Whether it is keyed as a scene document, without an extension, where otherwise by key and extension.</param>
internal sealed record AssetForm(string Type, string Read, bool Scene)
{
    /// <summary>The generated switch resolving a key to the member the build declared it on.</summary>
    internal string Lookup => "Find" + Read;
}

// Every member type a scene document writes in a form of the engine's own, and how each is read.
internal static class PropertyForms
{
    // A new built-in type is one entry here and one read method of the same name on AuthoredProperties.
    internal static readonly BuiltInForm[] BuiltIns =
    [
        new("bool", "Bool"),
        new("int", "Int"),
        new("float", "Float"),
        new("string", "String"),
        new("global::System.Numerics.Vector2", "Vector2"),
        new("global::Capsule.Rendering.Rect", "Rect"),
        new("global::Capsule.Rendering.ColorRgba", "Color"),
    ];

    // A new asset type is one entry here, one read method of the same name on AuthoredProperties, and the build's
    // CapsuleGeneratedAsset attribute on its CapsuleAssets members. Lookups are generated in this order.
    internal static readonly AssetForm[] Assets =
    [
        new("global::Capsule.Assets.TextureHandle", "Texture", Scene: false),
        new("global::Capsule.Audio.AudioClip", "Sound", Scene: false),
        new("global::Capsule.Scenes.SceneKey", "Scene", Scene: true),
    ];

    /// <summary>Every member type a placement can set, as the refusal of any other type names them.</summary>
    internal static readonly string Supported =
        $"Use {string.Join(", ", BuiltIns.Select(static form => form.Type).Concat(Assets.Select(static asset => asset.Type)).Select(static type => type.Replace("global::", string.Empty)))}, "
        + "an enum or a nullable of one of those, an Entity class or an interface, a class declaring [Authorable] members, "
        + "an array of any type here but a nullable or an array, "
        + "declare [JsonConverter(typeof(...))] on the type, or give a readonly struct, or a record class without settable members, "
        + "public static readonly fields of its own type for a document to name";

    internal static BuiltInForm? BuiltIn(string type) => Array.Find(BuiltIns, form => form.Type == type);

    internal static AssetForm? Asset(string type) => Array.Find(Assets, form => form.Type == type);
}
