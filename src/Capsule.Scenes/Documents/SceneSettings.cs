using System.Numerics;
using System.Text.Json;
using Capsule.Rendering;

namespace Capsule.Scenes.Documents;

/// <summary>
/// The scene-level state a document authors, one property per top-level key. A null property is not
/// authored and leaves the scene's own value in place.
/// </summary>
public sealed record SceneSettings
{
    /// <summary>The key of the abstract <see cref="Scene"/> subclass the composed scene derives from.</summary>
    public string? BaseScene { get; init; }

    /// <summary>
    /// The key of the concrete <see cref="Scenes.Camera"/> subclass that becomes the scene's <see cref="Scene.Camera"/>
    /// when the scene is composed through its generated registration.
    /// </summary>
    public string? Camera { get; init; }

    /// <summary>The scene's <see cref="Scene.Size"/> in world units, in place of the extent of its tile maps.</summary>
    public Vector2? Size { get; init; }

    /// <summary>The <see cref="Scenes.Camera.ScrollCenter"/> written to every camera installed in the scene.</summary>
    public Vector2? ScrollCenter { get; init; }

    /// <summary>The scene's <see cref="Scene.ClearColor"/>, which is opaque.</summary>
    public ColorRgba? ClearColor { get; init; }

    /// <summary>The scene's <see cref="Scene.Ambient"/>, which is opaque.</summary>
    public ColorRgba? Ambient { get; init; }

    /// <summary>The scene's <see cref="Scene.Sampling"/>, in place of the game's setting.</summary>
    public TextureSampling? Sampling { get; init; }

    /// <summary>
    /// The authored <c>properties</c> object, whose keys set the authorable members of the class composing
    /// the scene, camel-cased.
    /// </summary>
    public JsonElement? Properties { get; init; }
}
