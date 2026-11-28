using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;
using Capsule.UI;

namespace Capsule.Runtime.DevTools.Overlay;

// Draws the overlay. It is told what to show only on the frames that change it. The menu is a column of
// lines, each one line tall: the readout, the title, the rows, the status and the legend.
internal sealed class OverlayScene : Scene
{
    internal const int MaxRows = 24;

    // Between a backdrop's edge and its text, on every side.
    internal const int Padding = 4;

    // Columns for each of the pointer's world figures. A figure past ten million overruns them.
    private const int PointerFigureWidth = 9;

    // Two figures and the separator between them.
    private const int PointerLength = (PointerFigureWidth * 2) + 2;

    // In menu pixels, inside the right padding.
    private const int ScrollbarWidth = 2;

    internal static readonly BitmapFont Font = BitmapFont.Default;
    internal static readonly ColorRgba BackdropColor = ColorRgba.Black with { A = 160 };
    private static readonly ColorRgba TrackColor = ColorRgba.White with { A = 48 };
    private static readonly ColorRgba ThumbColor = ColorRgba.White with { A = 160 };

    // The pointer line is measured at its full width, blank or not. A first point never widens the menu.
    private static readonly float PointerWidth = Font.Measure(new string('0', PointerLength)).X;

    // The font is monospace. The pointer line is cut to the menu's width in these.
    private static readonly float GlyphWidth = Font.Measure("0").X;

    // The backdrop, the highlight and the scrollbar draw on the column itself and take no line.
    private readonly BoxContainer _menu;
    private readonly ColorRect _highlight;
    private readonly Label _sceneName;
    private readonly Label _tick;
    private readonly Label _pointer;
    private readonly Label _title;
    private readonly Label _status;
    private readonly Label _legend;
    private readonly ColorRect _track;
    private readonly ColorRect _thumb;
    private readonly Label[] _rows = new Label[MaxRows];

    // The blank line between a title and the rows. Without a title the empty title line is the gap.
    private readonly Entity _titleGap;

    // A value row's text as drawn. ShowPage grows it to the page's longest value row.
    private char[] _text = [];

    // The tick and pointer lines are written into these. Each holds its widest figures.
    private readonly char[] _tickText = new char[32];
    private readonly char[] _pointerText = new char[128];
    private Vector2? _pointerWorld;

    // The entity the pointer line names, and its name read once per change.
    private Entity? _under;
    private string? _underName;

    // The name _underName was spelled from, which a join can number afresh.
    private string? _underRequested;
    private int _underNumber;
    private (int X, int Y) _cell;

    // The last ShowPage and Place. RowAt reads them to turn a pointer into a row.
    private IReadOnlyList<OverlayRow> _page = [];
    private int _column;
    private int _top;
    private int _shown;
    private int _width;

    private bool _menuShown = true;
    private bool _paneShown;

    internal FramePane Pane { get; } = new();

    internal OverlayScene(string toggleName)
    {
        _menu = new BoxContainer(Axis.Vertical, Anchor.TopLeft, Vector2.Zero) { Padding = new Insets(Padding) };
        _menu.Add(new ColorRect(Vector2.Zero) { Color = BackdropColor });
        _highlight = new ColorRect(Vector2.Zero) { Color = ColorRgba.White with { A = 64 } };
        _track = new ColorRect(Vector2.Zero) { Color = TrackColor };
        _thumb = new ColorRect(Vector2.Zero) { Color = ThumbColor };
        _menu.Add(_highlight);
        _menu.Add(_track);
        _menu.Add(_thumb);

        // Added before the first frame. A later line would not show until a step settles it.
        _sceneName = AddLine();
        _tick = AddLine();
        _pointer = AddLine();
        _title = AddLine();
        _titleGap = AddLine().Entity!;
        // A row draws one line even when its text breaks one. RowAt counts on that.
        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index] = AddLine();
            _rows[index].MaxLines = 1;
        }

        AddLine();
        _status = AddLine();
        _legend = AddLine();
        _legend.Text = OverlayActions.Legend(toggleName);
        Add(_menu);
    }

    internal string[] Readout() => [_sceneName.Text, _tick.Text, _pointer.Text];

    internal string Status => _status.Text;

    // Both are empty while the page fits its window.
    internal Rect ScrollTrack => _track.Visible ? _track.Bounds : default;

    internal Rect ScrollThumb => _thumb.Visible ? _thumb.Bounds : default;

    internal Rect RowBounds(int row) => _rows[row].Bounds;

    internal string[] ShownRows()
    {
        string[] shown = new string[_shown];
        for (int index = 0; index < shown.Length; index++)
        {
            shown[index] = _rows[index].Text;
        }

        return shown;
    }

    // A withdrawn menu shows no rows. The pointer finds none until the page is laid out again.
    internal bool ShowMenu(bool shown)
    {
        bool changed = Show(_menu, ref _menuShown, shown);
        if (changed && !shown)
        {
            _shown = 0;
        }

        return changed;
    }

    // The menu's own switch does not touch the pane.
    internal void ShowFramePane(bool shown) => Show(Pane, ref _paneShown, shown);

    internal void SetReadout(string sceneName, long tick)
    {
        _sceneName.Text = sceneName;
        NumberText line = new(_tickText);
        line.Add("tick ");
        line.Add(tick);
        _tick.SetText(line.Written);
    }

    // The world point the pointer line shows, or null for a blank line before the pointer has stood over
    // the world. The figures are fixed-width, which keeps the line's width while the pointer moves. under is
    // the entity on top there, and cell the pointer's cell when under is a tile map. The line is cut at the
    // menu's width.
    internal void SetPointer(Vector2? world, Entity? under, (int X, int Y) cell)
    {
        bool renamed = under is not null && (under.NameNumber != _underNumber || !ReferenceEquals(under.RequestedName, _underRequested));
        if (world == _pointerWorld && ReferenceEquals(under, _under) && cell == _cell && !renamed)
        {
            return;
        }

        if (!ReferenceEquals(under, _under) || renamed)
        {
            _under = under;
            _underName = under?.Name;
            _underRequested = under?.RequestedName;
            _underNumber = under?.NameNumber ?? 0;
        }

        _pointerWorld = world;
        _cell = cell;
        NumberText line = new(_pointerText);
        if (world is { } point)
        {
            line.Add(point.X, "F1", PointerFigureWidth);
            line.Add(", ");
            line.Add(point.Y, "F1", PointerFigureWidth);
        }

        if (_underName is { } name)
        {
            line.Add("  ");
            line.Add(name);
        }

        if (under is TileMap map && cell.X >= 0 && cell.Y >= 0 && cell.X < map.Width && cell.Y < map.Height)
        {
            line.Add(" (");
            line.Add(cell.X);
            line.Add(", ");
            line.Add(cell.Y);
            line.Add(") ");
            line.Add(map.TileAt(cell.X, cell.Y).Name);
        }

        ReadOnlySpan<char> written = line.Written;
        int fits = _width > 0 ? Math.Max(PointerLength, (int)((_width - (Padding * 2)) / GlyphWidth)) : written.Length;
        _pointer.SetText(written[..Math.Min(written.Length, fits)]);
    }

    // Drops the rows and the entity it holds of a scene the run has left. The pointer line goes blank and
    // the pointer finds no row until the next page is shown.
    internal void ClearPage()
    {
        SetPointer(null, null, default);
        _page = [];
        _shown = 0;
    }

    // Whether a point on the overlay's canvas lies on the menu's backdrop.
    internal bool CoversMenu(Vector2 pointer)
    {
        if (!_menuShown)
        {
            return false;
        }

        Vector2 extent = _menu.Extent;

        return pointer.X >= 0f && pointer.X <= extent.X && pointer.Y >= 0f && pointer.Y <= extent.Y;
    }

    // The status row is one line. It shows the first line of text.
    internal void SetStatus(string text)
    {
        int end = text.AsSpan().IndexOfAny('\r', '\n');
        _status.Text = end < 0 ? text : text[..end];
    }

    internal void ShowPage(string? title, IReadOnlyList<OverlayRow> rows)
    {
        _page = rows;
        _title.Text = title ?? string.Empty;
        _titleGap.Visible = title is not null;
        _shown = Math.Min(rows.Count, MaxRows);

        // Two spaces past the widest valued label. Spaces align it because the font is monospace.
        _column = 0;
        int longestValue = 0;
        foreach (OverlayRow row in rows)
        {
            if (row.Value is { } value)
            {
                _column = Math.Max(_column, row.Label.Length + 2);
                longestValue = Math.Max(longestValue, value.Length);
            }
        }

        if (_column + longestValue > _text.Length)
        {
            _text = new char[_column + longestValue];
        }

        // Every row is measured, shown or not. The menu keeps one width as the window moves.
        float width = MathF.Max(
            MathF.Max(Font.Measure(_sceneName.Text).X, Font.Measure(_tick.Text).X),
            MathF.Max(PointerWidth, Font.Measure(_title.Text).X));
        width = MathF.Max(width, MathF.Max(Font.Measure(_status.Text).X, Font.Measure(_legend.Text).X));
        foreach (OverlayRow row in rows)
        {
            width = MathF.Max(width, Font.Measure(RowText(row)).X);
        }

        _width = (int)width + (Padding * 2);
        _menu.Size = new Vector2(_width, 0f);

        // The pointer line is cut again at the new width.
        _pointerWorld = null;
        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index].Entity!.Visible = index < _shown;
        }

        // Shown only past the window, in the right padding. The thumb is the window's share of the page.
        bool scrolls = rows.Count > MaxRows;
        float height = MaxRows * Font.LineHeight;
        _track.Offset = new Vector2(_width - Padding + ((Padding - ScrollbarWidth) / 2f), RowTop(0));
        _track.Size = new Vector2(ScrollbarWidth, height);
        _thumb.Size = new Vector2(ScrollbarWidth, height * _shown / rows.Count);
        _track.Visible = scrolls;
        _thumb.Visible = scrolls;

        // No window is shown yet.
        _top = -1;
    }

    // Rewrites the rows only when the window moved.
    internal void Place(int focus, int top)
    {
        if (top != _top)
        {
            _top = top;
            for (int index = 0; index < _shown; index++)
            {
                _rows[index].SetText(RowText(_page[top + index]));
            }
        }

        int focusedRow = focus - top;
        bool focused = focusedRow >= 0 && focusedRow < _shown && _page[focus].Activate is not null;
        _highlight.Visible = focused;
        if (focused)
        {
            _highlight.Offset = new Vector2(0f, RowTop(focusedRow));
            _highlight.Size = new Vector2(_width, Font.LineHeight);
        }

        if (_page.Count > MaxRows)
        {
            float height = MaxRows * Font.LineHeight;
            _thumb.Offset = _track.Offset + new Vector2(0f, height * top / _page.Count);
        }
    }

    // Returns -1 off the rows. The menu hangs top-left, which makes a canvas position a menu one.
    internal int RowAt(Vector2 pointer)
    {
        if (!_menuShown || _shown == 0 || pointer.X < 0f || pointer.X > _width)
        {
            return -1;
        }

        // The shown rows are one line each, stacked with no spacing under the first.
        int index = (int)MathF.Floor((pointer.Y - RowTop(0)) / Font.LineHeight);

        return index >= 0 && index < _shown ? _top + index : -1;
    }

    // A line one line tall at the column's next place, holding the label returned.
    private Label AddLine()
    {
        ScreenEntity line = new(Anchor.TopLeft, Vector2.Zero) { Parent = _menu, Size = new Vector2(0f, Font.LineHeight) };

        return line.Add(new Label(Font));
    }

    private bool Show(ScreenEntity entity, ref bool isShown, bool shown)
    {
        if (isShown == shown)
        {
            return false;
        }

        isShown = shown;
        if (shown)
        {
            Add(entity);
        }
        else
        {
            Remove(entity);
        }

        return true;
    }

    // Where the column laid out the row line at index, in menu pixels.
    private float RowTop(int index) => ClipRect.OuterRect(_rows[index].Entity!, previous: false).Top;

    private ReadOnlySpan<char> RowText(in OverlayRow row)
    {
        if (row.Value is not { } value)
        {
            return row.Label;
        }

        Span<char> text = _text;
        row.Label.CopyTo(text);
        text[row.Label.Length.._column].Fill(' ');
        value.CopyTo(text[_column..]);

        return text[..(_column + value.Length)];
    }
}
