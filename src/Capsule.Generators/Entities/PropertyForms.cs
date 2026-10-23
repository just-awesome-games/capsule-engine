namespace Capsule.Generators;

/// <summary>A type a scene document writes in a JSON form of the engine's own.</summary>
/// <param name="Type">The type as the generator displays it fully qualified.</param>
/// <param name="Read">The <c>AuthoredMembers</c> method the spawner reads it with.</param>
internal sealed record BuiltInForm(string Type, string Read);

/// <summary>An asset type a scene document names by key, resolved at load through a generated lookup.</summary>
/// <param name="Type">The type as the generator displays it fully qualified.</param>
/// <param name="Read">The <c>AuthoredMembers</c> method the spawner reads it with, which also names its lookup.</param>
/// <param name="Table">The metadata name of the build's generated class that searches its keys.</param>
/// <param name="Find">The member of <paramref name="Table"/> that searches them.</param>
internal sealed record AssetForm(string Type, string Read, string Table, string Find)
{
    /// <summary>The generated method resolving a key to its asset.</summary>
    internal string Lookup => "Find" + Read;
}

// Every member type a scene document writes in a form of the engine's own, and how each is read.
internal static class PropertyForms
{
    // A new built-in type is one entry here and one read method of the same name on AuthoredMembers.
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

    // A new asset type is one entry here, one read method of the same name on AuthoredMembers, and a search the
    // build writes. Lookups are generated in this order. Each searches data the build wrote, never a switch over
    // the keys: a game's assets reach thousands and its texts tens of thousands, and a string switch that long
    // takes minutes to compile.
    internal static readonly AssetForm[] Assets =
    [
        new("global::Capsule.Assets.TextureHandle", "Texture", MetadataNames.CapsuleAssetIndex, "FindTexture"),
        new("global::Capsule.Audio.AudioClip", "Sound", MetadataNames.CapsuleAssetIndex, "FindSound"),
        new("global::Capsule.Scenes.SceneKey", "Scene", MetadataNames.CapsuleAssetIndex, "FindScene"),
        new("global::Capsule.Localization.TextKey", "Text", MetadataNames.CapsuleStrings, "Table.Find"),
    ];

    /// <summary>Every member type a placement can set, as the refusal of any other type names them.</summary>
    internal static readonly string Supported =
        $"Use {string.Join(", ", BuiltIns.Select(static form => form.Type).Concat(Assets.Select(static asset => asset.Type)).Select(static type => type.Replace("global::", string.Empty)))}, "
        + "an enum or a nullable of one of those, an Entity class or an interface, a class declaring [Authorable] members, "
        + "an array of any type here but a nullable or an array, a ReadOnlyMemory of a built-in type, an enum or a definition, "
        + "a type declaring [JsonConverter(typeof(...))], or a readonly struct or a record class without settable members "
        + "whose public static readonly fields of its own type a document names";

    internal static BuiltInForm? BuiltIn(string type) => Array.Find(BuiltIns, form => form.Type == type);

    internal static AssetForm? Asset(string type) => Array.Find(Assets, form => form.Type == type);
}
