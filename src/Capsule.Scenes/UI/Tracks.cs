using System.Numerics;
using Capsule.Scenes;

namespace Capsule.UI;

// The one track solver both containers place their children with. A grid fills rows of Columns cells,
// and each column is as wide as its widest cell and each row as tall as its tallest. A box is a grid of
// one column or one unbounded row whose single cross track spans the padded inside. The columns solve
// first, under the width cap from above, and the rows then solve for the owner's width, since a child's
// height can depend on its width. Each solve is cached against what it read, and its storage grows only
// with the child count.
internal sealed class Tracks
{
    // Offset from the padded inside's near edge and size of each column, then of each row.
    private float[] _columnOffsets = [];
    private float[] _columnSizes = [];
    private float[] _rowOffsets = [];
    private float[] _rowSizes = [];

    // The tracks' total extents with the owner's padding.
    private float _width;
    private float _height;

    // What the cached solves read: the width cap and the run's text version the columns solved under, and
    // the owner width the rows solved at, which is NaN until they solve again.
    private float _cap;
    private int _text;
    private float _rowsAt = float.NaN;

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

    // The columns' extent with the padding, under the width cap `given` from above.
    internal float Width(ScreenEntity owner, float given)
    {
        SolveColumns(owner, given);

        return _width;
    }

    // The rows' extent with the padding, once the owner is `width` wide.
    internal float Height(ScreenEntity owner, float width, float given)
    {
        SolveRows(owner, width, given);

        return _height;
    }

    // The cell's corner in the owner's units, and its extent. A hidden child takes an empty slot at the
    // padded inside's corner.
    internal Vector2 Slot(ScreenEntity owner, ScreenEntity child, Vector2 extent, float given, out Vector2 slot)
    {
        SolveRows(owner, extent.X, given);

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

    private void SolveColumns(ScreenEntity owner, float given)
    {
        int text = owner.SceneOrNull?.RunOrNull?.TextVersion ?? 0;
        if (!_stale && given == _cap && text == _text)
        {
            return;
        }

        int cells = 0;
        int columns = 0;
        int rows = 0;
        float cap = owner.Inner(given);

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

            // Cells fill in order. A new track is always the next one.
            if (column == columns)
            {
                Open(ref _columnOffsets, ref _columnSizes, columns++);
            }

            if (row == rows)
            {
                Open(ref _rowOffsets, ref _rowSizes, rows++);
            }

            _columnSizes[column] = Math.Max(_columnSizes[column], child.PreferredWidth(cap));
        }

        Insets padding = owner.Padding;
        _width = Lay(_columnOffsets, _columnSizes, columns, Spacing.X) + padding.Left + padding.Right;
        _cap = given;
        _text = text;
        _rowsAt = float.NaN;
        _stale = false;
    }

    // A child's width in its cell is its share of the slot on an axis it spans, and its own otherwise. The
    // slot is the padded inside across a box that spans it, and the column in any other.
    private void SolveRows(ScreenEntity owner, float width, float given)
    {
        SolveColumns(owner, given);
        if (width == _rowsAt)
        {
            return;
        }

        int rows = 0;
        float cap = owner.Inner(given);
        Insets padding = owner.Padding;
        float inside = Math.Max(width - padding.Left - padding.Right, 0f);

        foreach (Entity entity in owner.Children)
        {
            if (entity is not ScreenEntity { Cell: >= 0 } child)
            {
                continue;
            }

            int row = child.Cell / Columns;
            if (row == rows)
            {
                _rowSizes[rows++] = 0f;
            }

            float slot = Spans == Axis.Horizontal ? inside : _columnSizes[child.Cell % Columns];
            _rowSizes[row] = Math.Max(_rowSizes[row], child.PreferredHeight(child.WidthIn(slot, cap), cap));
        }

        _height = Lay(_rowOffsets, _rowSizes, rows, Spacing.Y) + padding.Top + padding.Bottom;
        _rowsAt = width;
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
