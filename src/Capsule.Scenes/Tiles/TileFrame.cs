using System.ComponentModel;
using Capsule.Scenes;

namespace Capsule.Tiles;

/// <summary>One frame of an animated <see cref="TileType"/>: a cell of the map's texture and how many fixed steps it is held.</summary>
/// <param name="Cell">The cell of the map's texture this frame draws, counted as <see cref="TileType.Cell"/> is.</param>
/// <param name="Ticks">How many fixed steps the frame is held, at least 1.</param>
public sealed record TileFrame(
    [property: Authorable(Required = true)] int Cell,
    [property: Authorable(Required = true)] int Ticks)
{
    /// <summary>A frame of cell 0 held for no steps, which a scene document then fills.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TileFrame()
        : this(0, 0)
    {
    }
}
