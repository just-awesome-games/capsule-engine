using System.ComponentModel;
using Capsule.Scenes;

namespace Capsule.Tiles;

/// <summary>One look of a rule tile: the cell it draws where exactly these neighbours join it.</summary>
/// <param name="Cell">The cell of the map's texture this look draws, counted as <see cref="TileType.Cell"/> is.</param>
/// <param name="Joined">
/// The sides whose neighbours join the cell, out of every side the tile type's rules name. A side no rule names is
/// never read. <see cref="TileSides.None"/> is the look of a cell nothing joins.
/// </param>
public sealed record TileRule(
    [property: Authorable(Required = true)] int Cell,
    [property: Authorable] TileSides Joined = TileSides.None)
{
    /// <summary>A look of cell 0 that nothing joins, which a scene document then fills.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TileRule()
        : this(0)
    {
    }
}
