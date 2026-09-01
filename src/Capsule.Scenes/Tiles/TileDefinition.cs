using Capsule.Collision;
using Capsule.Rendering;

namespace Capsule.Scenes.Tiles;

/// <summary>
/// One entry of a grid's tile palette: its semantic type, an optional colour presentation, and what
/// it collides as. A missing colour leaves presentation to another renderer without weakening tile
/// identity, and a tile type collides as nothing until it names a layer.
/// </summary>
/// <param name="Type">The tile type's name, unique within the palette. Identity, never a layer.</param>
/// <param name="Color">The colour a tile of this type draws as, or null to draw nothing.</param>
/// <param name="Layer">The collision layer a tile of this type is on; null means it collides as nothing.</param>
/// <param name="CollidableFaces">Which sides of the tile collide; every side by default.</param>
public readonly record struct TileDefinition(
    string Type,
    ColorRgba? Color,
    string? Layer = null,
    CellFaces2D CollidableFaces = CellFaces2D.All);
