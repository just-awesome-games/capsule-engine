using Capsule.Collision;
using Capsule.Rendering;

namespace Capsule.Scenes.Tiles;

/// <summary>
/// One entry of a grid's tile palette: its semantic type, an optional colour presentation, and
/// what it collides as. A missing colour leaves presentation to another renderer without weakening
/// tile identity, and a tile type collides as nothing until it says otherwise.
/// </summary>
/// <param name="Type">The tile type's name, unique within the palette and the tag its cells carry.</param>
/// <param name="Color">The colour a tile of this type draws as, or null to draw nothing.</param>
/// <param name="Collision">The shape a tile of this type contributes to the map's collider.</param>
public readonly record struct TileDefinition(
    string Type,
    ColorRgba? Color,
    TileCollision Collision = TileCollision.None);
