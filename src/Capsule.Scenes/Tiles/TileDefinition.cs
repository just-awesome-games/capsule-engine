using Capsule.Rendering;

namespace Capsule.Scenes.Tiles;

/// <summary>
/// One entry of a grid's tile palette: its semantic type and an optional colour presentation.
/// A missing colour leaves presentation to another renderer without weakening tile identity.
/// </summary>
public readonly record struct TileDefinition(string Type, ColorRgba? Color);
