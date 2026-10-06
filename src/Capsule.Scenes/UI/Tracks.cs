using System.Numerics;
using Capsule.Scenes;

namespace Capsule.UI;

// The one track solver both containers place their children with. A grid fills rows of Columns cells,
// and each column is as wide as its widest cell and each row as tall as its tallest. A box is a grid of
// one column or one unbounded row whose single cross track spans the padded inside. The solve is cached
// and runs again on the first read after an invalidation. Its storage grows only with the child count.
internal sealed class Tracks
{
    // Offset from the padded inside's near edge and size of each column, then of each row.
    private float[] _columnOffsets = [];
    private float[] _columnSizes = [];
    private float[] _rowOffsets = [];
    private float[] _rowSizes = [];

    private Vector2 _preferred;

    // Invariant: while this is true, a container parent the owner takes a cell in is stale too, and so on
    // up. A parent's solve reads every visible child's fit, and a hidden child takes no cell until its
    // Visible write reflows the parent. Reflow relies on this to stop early.
    private bool _stale = true;

    // A box is a grid of one column, or of int.MaxValue columns, which is one row.
    internal int Columns { get; set; }

    internal Vector2 Spacing { get; set; }

    // The box's cross axis, whose slot is the whole padded inside and not the track. Null on a grid.
    internal Axis? Spans { get; set; }

    // Marks the solve stale. Returns false when it already was, which ends a reflow walk.
    internal bool Invalidate()
    {
        if (_stale)
        {
            return false;
        }

        _stale = true;

        return true;
    }

    internal Vector2 Preferred(ScreenEntity owner)
    {
        if (_stale)
        {
            Solve(owner);
        }

        return _preferred;
    }

    // The cell's corner in the owner's units, and its extent. A hidden child takes an empty slot at the
    // padded inside's corner.
    internal Vector2 Slot(ScreenEntity owner, ScreenEntity child, Vector2 extent, out Vector2 slot)
    {
        if (_stale)
        {
            Solve(owner);
        }

        Insets padding = owner.Padding;
        Vector2 corner = new(padding.Left, padding.Top);
        int cell = child.Cell;

        if (cell < 0)
        {
            slot = Vector2.Zero;

            return corner;
        }

        int column = cell % Columns;
        int row = cell / Columns;
        slot = new Vector2(_columnSizes[column], _rowSizes[row]);

        if (Spans == Axis.Horizontal)
        {
            slot.X = padding.Inside(extent).X;
        }
        else if (Spans == Axis.Vertical)
        {
            slot.Y = padding.Inside(extent).Y;
        }

        return corner + new Vector2(_columnOffsets[column], _rowOffsets[row]);
    }

    private void Solve(ScreenEntity owner)
    {
        int cells = 0;
        int columns = 0;
        int rows = 0;

        foreach (Entity entity in owner.Children)
        {
            if (entity is not ScreenEntity child)
            {
                continue;
            }

            if (!child.Visible)
            {
                child.Cell = -1;
                continue;
            }

            int column = cells % Columns;
            int row = cells / Columns;
            child.Cell = cells++;

            // Cells fill in order, so a new track is always the next one.
            if (column == columns)
            {
                Open(ref _columnOffsets, ref _columnSizes, columns++);
            }

            if (row == rows)
            {
                Open(ref _rowOffsets, ref _rowSizes, rows++);
            }

            Vector2 preferred = child.Preferred;
            _columnSizes[column] = Math.Max(_columnSizes[column], preferred.X);
            _rowSizes[row] = Math.Max(_rowSizes[row], preferred.Y);
        }

        Vector2 content = new(Lay(_columnOffsets, _columnSizes, columns, Spacing.X), Lay(_rowOffsets, _rowSizes, rows, Spacing.Y));
        Insets padding = owner.Padding;
        _preferred = Vector2.Max(owner.Size, content + new Vector2(padding.Left + padding.Right, padding.Top + padding.Bottom));
        _stale = false;
    }

    // Zeroes track `index`, growing the storage when it is full.
    private static void Open(ref float[] offsets, ref float[] sizes, int index)
    {
        if (index == sizes.Length)
        {
            int length = Math.Max(4, sizes.Length * 2);
            Array.Resize(ref offsets, length);
            Array.Resize(ref sizes, length);
        }

        sizes[index] = 0f;
    }

    // Writes each track's offset and returns the tracks' total extent with the spacing between them.
    private static float Lay(float[] offsets, float[] sizes, int count, float spacing)
    {
        float along = 0f;

        for (int index = 0; index < count; index++)
        {
            offsets[index] = along;
            along += sizes[index] + spacing;
        }

        return count == 0 ? 0f : along - spacing;
    }
}
