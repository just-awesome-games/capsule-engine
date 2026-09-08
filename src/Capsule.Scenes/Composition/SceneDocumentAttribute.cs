namespace Capsule.Scenes;

/// <summary>
/// Overrides the document key a scene derives from where its class is declared. The value is a
/// whole key — '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them
/// a reserved Windows device name, no extension — unique across the game's logic assemblies.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SceneDocumentAttribute : Attribute
{
    /// <param name="name">The document's key under the scenes root, without the <c>.scene.json</c> suffix.</param>
    public SceneDocumentAttribute(string name) => Name = name;

    /// <summary>The document's key under the scenes root, without the <c>.scene.json</c> suffix.</summary>
    public string Name { get; }
}
