namespace Capsule.Scenes;

/// <summary>
/// Overrides the document key a scene would otherwise derive from its namespace. The value is a complete key,
/// unique across the game's logic assemblies: '/'-joined segments of ASCII letters, digits, hyphens and
/// underscores, with no segment a reserved Windows device name and no file extension.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SceneDocumentAttribute : Attribute
{
    /// <param name="name">The document's key, its path under <c>Assets/</c> without the <c>.scene.json</c> suffix.</param>
    public SceneDocumentAttribute(string name) => Name = name;

    /// <summary>The document's key, its path under <c>Assets/</c> without the <c>.scene.json</c> suffix.</summary>
    public string Name { get; }
}
