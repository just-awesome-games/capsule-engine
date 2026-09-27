namespace Capsule.Scenes;

/// <summary>The key of a scene document, as <c>CapsuleAssets.Scenes</c> names each one.</summary>
/// <param name="Name">The document's path under <c>Assets/</c>, keyed and without <c>.scene.json</c>: <c>scenes/halls/hall</c>.</param>
public readonly record struct SceneKey(string Name)
{
    /// <summary>Returns <see cref="Name"/>.</summary>
    public override string ToString() => Name;

    // A default key names no document, and a request taking one fails at the call rather than at the transition.
    internal string Required(string parameter) =>
        string.IsNullOrWhiteSpace(Name)
            ? throw new ArgumentException("The scene key names no document. Pass a CapsuleAssets.Scenes member.", parameter)
            : Name;
}
