using System.ComponentModel;

namespace Capsule.Scenes;

/// <summary>Sets the authorable members of a scene class that the document's top-level keys author.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void SceneApplier(Scene scene, AuthoredMembers members);

/// <summary>The scene document and entity registry a <see cref="Scene"/> is composed from.</summary>
/// <param name="Document">The document to compose.</param>
/// <param name="Entities">The registry saying what each type key constructs.</param>
/// <param name="DocumentKey">
/// The document's key, which the composed scene reports as <see cref="Scene.DocumentKey"/>, or null for a document built
/// by hand. The registry sets it for every document it composes.
/// </param>
/// <param name="Apply">
/// The applier of the composed class's authorable members, or null when it declares none. Only generated
/// code sets this.
/// </param>
public readonly record struct SceneContent(
    SceneDocument Document,
    EntityRegistry Entities,
    SceneKey? DocumentKey = null,
    [property: EditorBrowsable(EditorBrowsableState.Never)] SceneApplier? Apply = null);
