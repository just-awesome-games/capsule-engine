namespace Capsule.Text;

/// <summary>
/// A string resolved to the set of filled cells on a glyph grid. Dimensions and
/// cell coordinates are in grid cells, not pixels — the renderer chooses the scale.
/// </summary>
public sealed class PixelTextLayout
{
    private readonly GridCell[] _cells;

    internal PixelTextLayout(GridCell[] cells, int width, int height)
    {
        _cells = cells;
        Width = width;
        Height = height;
    }

    public ReadOnlySpan<GridCell> Cells => _cells;

    /// <summary>Grid columns spanned, including inter-glyph spacing but not trailing spacing.</summary>
    public int Width { get; }

    /// <summary>Grid rows spanned; zero for an empty string.</summary>
    public int Height { get; }

    public int CellCount => _cells.Length;
}
