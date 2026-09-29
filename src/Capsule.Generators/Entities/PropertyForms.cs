namespace Capsule.Generators;

/// <summary>A type a scene document writes in a JSON form of the engine's own.</summary>
/// <param name="Type">The type as the generator displays it fully qualified.</param>
/// <param name="Read">The <c>AuthoredProperties</c> method the spawner reads it with.</param>
/// <param name="Form">What to write, as a build failure names it.</param>
/// <param name="Accepts">Whether a value, as the build's placement attribute carries it, is in this form.</param>
internal sealed record BuiltInForm(string Type, string Read, string Form, Func<object?, bool> Accepts);

/// <summary>An asset type a scene document names by key, resolved at load through a generated lookup.</summary>
/// <param name="Type">The type as the generator displays it fully qualified.</param>
/// <param name="Read">The <c>AuthoredProperties</c> method the spawner reads it with, which also names its lookup.</param>
/// <param name="Form">What to write, as a build failure names it.</param>
/// <param name="Fix">What to write in place of a key the build did not declare.</param>
/// <param name="Scene">Whether it is keyed as a scene document, without an extension, where otherwise by key and extension.</param>
internal sealed record AssetForm(string Type, string Read, string Form, string Fix, bool Scene)
{
    /// <summary>The generated switch resolving a key to the member the build declared it on.</summary>
    internal string Lookup => "Find" + Read;
}

// Every member type a scene document writes in a form of the engine's own, and how each is read.
internal static class PropertyForms
{
    /// <summary>What an entity reference is written as, as a build failure names it.</summary>
    internal const string ReferenceForm = "an entity id, a whole number";

    // A new built-in type is one entry here and one read method of the same name on AuthoredProperties.
    internal static readonly BuiltInForm[] BuiltIns =
    [
        new("bool", "Bool", "true or false", static value => value is bool),
        new("int", "Int", "a whole number in int range", static value => value is int),
        new("float", "Float", "a finite number", IsFloat),
        new("string", "String", "a string in quotes", static value => value is string),
        new("global::System.Numerics.Vector2", "Vector2", "[x, y] with both numbers finite", static value =>
            value is EquatableArray<object?> { Items: { Length: 2 } pair } && IsFloat(pair[0]) && IsFloat(pair[1])),
        new("global::Capsule.Rendering.ColorRgba", "Color", "\"#rrggbb\" or \"#rrggbbaa\"", static value =>
            value is string text && text.Length is 7 or 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit)),
    ];

    // A new asset type is one entry here, one read method of the same name on AuthoredProperties, and the build's
    // CapsuleGeneratedAsset attribute on its CapsuleAssets members. Lookups are generated in this order.
    internal static readonly AssetForm[] Assets =
    [
        new("global::Capsule.Assets.TextureHandle", "Texture", "a texture's key and extension in quotes, as \"textures/hazard.png\"",
            "Write the key and extension of a texture under Assets/", Scene: false),
        new("global::Capsule.Audio.AudioClip", "Sound", "a sound's key and extension in quotes, as \"audio/step.wav\"",
            "Write the key and extension of a sound under Assets/", Scene: false),
        new("global::Capsule.Scenes.SceneKey", "Scene", "a scene document's key in quotes, as \"scenes/halls/hall\"",
            "Write the key of a scene document under Assets/, with no extension", Scene: true),
    ];

    /// <summary>Every member type a placement can set, as the refusal of any other type names them.</summary>
    internal static readonly string Supported =
        $"Use {string.Join(", ", BuiltIns.Select(static form => form.Type).Concat(Assets.Select(static asset => asset.Type)).Select(static type => type.Replace("global::", string.Empty)))}, "
        + "an enum or a nullable of one of those, an Entity class or an interface, an array of any type here but a nullable or an array, "
        + "declare [JsonConverter(typeof(...))] on the type, or give a readonly struct, or a record class without settable members, "
        + "public static readonly fields of its own type for a document to name";

    internal static BuiltInForm? BuiltIn(string type) => Array.Find(BuiltIns, form => form.Type == type);

    internal static AssetForm? Asset(string type) => Array.Find(Assets, form => form.Type == type);

    private static bool IsFloat(object? value) => value is int || (value is double number && !float.IsInfinity((float)number));
}
