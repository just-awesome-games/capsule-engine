using Capsule.Rendering;

namespace Capsule.Maps;

/// <summary>
/// One entry of a grid's tile palette: what the tile is, and what it looks like. The colour is
/// absent only for the reserved empty entry, which is never drawn.
/// </summary>
public readonly record struct TileDefinition(string Type, ColorRgba? Color);
