namespace Capsule.Scenes;

/// <summary>
/// Overrides the document name a scene derives from its class name. The value must be a portable
/// file stem and is unique across the game's logic assemblies.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SceneDocumentAttribute : Attribute
{
    /// <param name="name">The portable document file stem, without the <c>.scene.json</c> suffix.</param>
    public SceneDocumentAttribute(string name) => Name = name;

    /// <summary>The portable document file stem, without the <c>.scene.json</c> suffix.</summary>
    public string Name { get; }
}
