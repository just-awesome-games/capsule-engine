using System.ComponentModel;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>The scene document and entity registry a <see cref="Scene"/> is composed from.</summary>
/// <param name="Document">The document to compose.</param>
/// <param name="Entities">The registry saying what each spawn type constructs.</param>
/// <param name="Camera">
/// The factory for the document's <see cref="SceneSettings.Camera"/>, resolved at generation time, or
/// null when the document names none. Only generated code sets this.
/// </param>
public readonly record struct SceneContent(
    SceneDocument Document,
    EntityRegistry Entities,
    [property: EditorBrowsable(EditorBrowsableState.Never)] Func<Camera>? Camera = null);
