using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

// The one track solver both containers place their children with. A grid fills rows of Columns cells,
// and each column is as wide as its widest cell and each row as tall as its tallest. A box is a grid of
// one column or one unbounded row whose single cross track spans the padded inside. The columns solve
// first, under the width cap from above, and the rows then solve for the owner's width, since a child's
// height can depend on its width. A flow is a grid whose every cell is its own column, and whose rows
// break where the next cell would pass the inside: under the width cap for the width it prefers, and at
// the width it takes for its rows. Each solve is cached against what it read, and its storage grows only
// with the child count.
internal sealed class Tracks
{
    // Pixels a flow's row may reach past the inside, or fall short of it, and still count as fitting it. A
    // flow taking the width it prefers then breaks and aligns its rows where its own sum put them.
    private const float Slack = 1e-3f;

    // Offset from the padded inside's near edge and size of each column, then of each row.
    private float[] _columnOffsets = [];
    private float[] _columnSizes = [];
    private float[] _rowOffsets = [];
    private float[] _rowSizes = [];

    // Each cell's row in a flow, and the number of visible cells in the last solve.
    private int[] _cellRows = [];
    private int _cells;

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

    // Whether the rows break at the inside's width, and where each row then sits across it.
    internal bool Flows { get; init; }

    internal HorizontalAlignment Alignment { get; set; }

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

        int column = Column(cell);
        int row = Row(cell);
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
        if (Flows)
        {
            Reserve(owner.Children.Length);
        }

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

            if (Flows)
            {
                _columnSizes[cells] = child.PreferredWidth(cap);
                child.Cell = cells++;
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
        _cells = cells;
        _width = (Flows ? Break(cap, place: false) : Lay(_columnOffsets, _columnSizes, columns, Spacing.X)) + padding.Left + padding.Right;
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
        if (Flows)
        {
            Break(inside, place: true);
        }

        foreach (Entity entity in owner.Children)
        {
            if (entity is not ScreenEntity { Cell: >= 0 } child)
            {
                continue;
            }

            int row = Row(child.Cell);
            if (row == rows)
            {
                _rowSizes[rows++] = 0f;
            }

            float slot = Spans == Axis.Horizontal ? inside : _columnSizes[Column(child.Cell)];
            _rowSizes[row] = Math.Max(_rowSizes[row], child.PreferredHeight(child.WidthIn(slot, cap), cap));
        }

        _height = Lay(_rowOffsets, _rowSizes, rows, Spacing.Y) + padding.Top + padding.Bottom;
        _rowsAt = width;
    }

    // Sizes a flow's storage for every child it holds, shown or not. A child shown later then takes a cell
    // without growing it, and a flow has no more rows than cells.
    private void Reserve(int children)
    {
        if (_cellRows.Length >= children)
        {
            return;
        }

        Array.Resize(ref _columnOffsets, children);
        Array.Resize(ref _columnSizes, children);
        Array.Resize(ref _rowOffsets, children);
        Array.Resize(ref _rowSizes, children);
        Array.Resize(ref _cellRows, children);
    }

    private int Column(int cell) => Flows ? cell : cell % Columns;

    private int Row(int cell) => Flows ? _cellRows[cell] : cell / Columns;

    // Breaks a flow's cells into rows no wider than `inside` and returns the widest row. A row holds at least
    // one cell. With `place`, writes each cell's row and its offset across the inside.
    private float Break(float inside, bool place)
    {
        float widest = 0f;
        float along = 0f;
        int start = 0;
        int row = 0;

        for (int cell = 0; cell < _cells; cell++)
        {
            float at = cell == start ? 0f : along + Spacing.X;
            if (cell > start && at + _columnSizes[cell] > inside + Slack)
            {
                widest = Math.Max(widest, along);
                if (place)
                {
                    Align(start, cell, inside - along);
                }

                start = cell;
                row++;
                at = 0f;
            }

            if (place)
            {
                _columnOffsets[cell] = at;
                _cellRows[cell] = row;
            }

            along = at + _columnSizes[cell];
        }

        if (place)
        {
            Align(start, _cells, inside - along);
        }

        return Math.Max(widest, along);
    }

    // Moves the cells from `start` up to `end`, a row with `slack` to spare, as Alignment says. An odd pixel
    // of a centred row's slack goes to the right.
    private void Align(int start, int end, float slack)
    {
        if (Math.Abs(slack) <= Slack)
        {
            return;
        }

        float shift = Alignment switch
        {
            HorizontalAlignment.Center => MathF.Floor(slack / 2f),
            HorizontalAlignment.Right => slack,
            _ => 0f,
        };

        for (int cell = start; cell < end; cell++)
        {
            _columnOffsets[cell] += shift;
        }
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
