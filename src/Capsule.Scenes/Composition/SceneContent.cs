using System.ComponentModel;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tiles;

namespace Capsule.Scenes;

/// <summary>Sets the authorable members of a scene class that the document's <c>properties</c> author.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void SceneApplier(Scene scene, AuthoredProperties properties);

/// <summary>
/// Builds the <see cref="TileType"/> subclass claiming <paramref name="type"/> with the engine's fields of
/// <paramref name="tile"/>, and sets the authorable members the palette entry's properties author.
/// </summary>
/// <returns>The composed tile type, or null when no class claims <paramref name="type"/>.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate TileType? TileTypeComposer(string type, TileType tile, AuthoredProperties properties);

/// <summary>The scene document and entity registry a <see cref="Scene"/> is composed from.</summary>
/// <param name="Document">The document to compose.</param>
/// <param name="Entities">The registry saying what each spawn type constructs.</param>
/// <param name="Camera">
/// The factory for the document's <see cref="SceneSettings.Camera"/>, resolved at generation time, or
/// null when the document names none. Only generated code sets this.
/// </param>
/// <param name="Apply">
/// The applier of the composed class's authorable members, or null when it declares none. Only generated
/// code sets this.
/// </param>
/// <param name="TileTypes">
/// The composer of every palette entry naming a <see cref="TileType"/> subclass, or null to compose each
/// entry as a plain <see cref="TileType"/>. Only generated code sets this.
/// </param>
public readonly record struct SceneContent(
    SceneDocument Document,
    EntityRegistry Entities,
    [property: EditorBrowsable(EditorBrowsableState.Never)] Func<Camera>? Camera = null,
    [property: EditorBrowsable(EditorBrowsableState.Never)] SceneApplier? Apply = null,
    [property: EditorBrowsable(EditorBrowsableState.Never)] TileTypeComposer? TileTypes = null);
