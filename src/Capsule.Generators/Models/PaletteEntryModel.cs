namespace Capsule.Generators;

/// <summary>
/// One palette entry of a shipped document that names a class or authors properties, as its
/// <c>CapsuleGeneratedTileType</c> attribute carries it.
/// </summary>
/// <param name="Id">The id of the tile-map entry holding the palette.</param>
/// <param name="Name">The palette entry's name.</param>
/// <param name="Type">The key of the tile type it composes, or null for a plain <c>TileType</c>.</param>
/// <param name="Properties">Each authored property with its value, in the form <see cref="PlacementModel.Properties"/> carries.</param>
/// <param name="Line">The line the tile-map entry starts on in the document's file, from 1, or 0 when unknown.</param>
/// <param name="Column">The column the tile-map entry starts at, from 1, or 0 when unknown.</param>
internal readonly record struct PaletteEntryModel(
    int Id,
    string Name,
    string? Type,
    EquatableArray<(string Name, object? Value)> Properties,
    int Line,
    int Column);
