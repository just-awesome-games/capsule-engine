using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// Draws the overlay. It is told what to show only on the frames that change it.
internal sealed class OverlayScene : Scene
{
    internal const int MaxRows = 24;

    // Between a backdrop's edge and its text, on every side.
    internal const int Padding = 4;

    // Scene, tick and pointer.
    private const int ReadoutLines = 3;

    // Blank, status, legend.
    private const int RowsBelowItems = 3;

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

    // The pointer line is measured at its full width, blank or not, so a first point never widens the menu.
    private static readonly float PointerWidth = Font.Measure(new string('0', PointerLength)).X;

    private readonly ScreenEntity _menu;
    private readonly ColorRect _backdrop;
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

    // A value row's text as drawn. ShowPage grows it to the page's longest value row.
    private char[] _text = [];

    // The tick and pointer lines are written into these. Each holds its widest figures.
    private readonly char[] _tickText = new char[32];
    private readonly char[] _pointerText = new char[64];
    private Vector2? _pointerWorld;

    // The last ShowPage and Place. RowAt reads them to turn a pointer into a row.
    private IReadOnlyList<OverlayRow> _page = [];
    private int _column;
    private int _top;
    private int _shown;
    private int _rowsAbove;
    private int _width;

    private bool _menuShown = true;
    private bool _paneShown;

    internal FramePane Pane { get; } = new();

    internal OverlayScene(string toggleName)
    {
        Sampling = TextureSampling.Point;

        _menu = new ScreenEntity(Anchor.TopLeft, Vector2.Zero);
        _backdrop = new ColorRect(Vector2.Zero) { Color = BackdropColor };
        _highlight = new ColorRect(Vector2.Zero) { Color = ColorRgba.White with { A = 64 } };
        _sceneName = new Label(Font);
        _tick = new Label(Font);
        _pointer = new Label(Font);
        _title = new Label(Font);
        _status = new Label(Font);
        _legend = new Label(Font, $"[{toggleName}] close   [Up/Dn] move   [Enter] select   [Bksp/Left] back");
        _track = new ColorRect(Vector2.Zero) { Color = TrackColor };
        _thumb = new ColorRect(Vector2.Zero) { Color = ThumbColor };

        _menu.Add(_backdrop);
        _menu.Add(_highlight);
        _menu.Add(_sceneName);
        _menu.Add(_tick);
        _menu.Add(_pointer);
        _menu.Add(_title);

        // Added before the first frame. A later label would not show until a step settles it.
        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index] = new Label(Font);
            _menu.Add(_rows[index]);
        }

        _menu.Add(_status);
        _menu.Add(_legend);
        _menu.Add(_track);
        _menu.Add(_thumb);
        Add(_menu);
    }

    internal string[] Readout() => [_sceneName.Text, _tick.Text, _pointer.Text];

    internal string Status => _status.Text;

    // Both are empty while the page fits its window.
    internal Rect ScrollTrack => _track.Bounds;

    internal Rect ScrollThumb => _thumb.Bounds;

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
    // the world. The figures are fixed-width, which keeps the line's width while the pointer moves.
    internal void SetPointer(Vector2? world)
    {
        if (world == _pointerWorld)
        {
            return;
        }

        _pointerWorld = world;
        NumberText line = new(_pointerText);
        if (world is { } point)
        {
            line.Add(point.X, "F1", PointerFigureWidth);
            line.Add(", ");
            line.Add(point.Y, "F1", PointerFigureWidth);
        }

        _pointer.SetText(line.Written);
    }

    // Whether a point on the overlay's canvas lies on the menu's backdrop.
    internal bool CoversMenu(Vector2 pointer) =>
        _menuShown
        && pointer.X >= 0f && pointer.X <= _backdrop.Size.X
        && pointer.Y >= 0f && pointer.Y <= _backdrop.Size.Y;

    // The status row is one line, so it shows the first line of text.
    internal void SetStatus(string text)
    {
        int end = text.AsSpan().IndexOfAny('\r', '\n');
        _status.Text = end < 0 ? text : text[..end];
    }

    internal void ShowPage(string? title, IReadOnlyList<OverlayRow> rows)
    {
        _page = rows;
        _title.Text = title ?? string.Empty;
        _rowsAbove = ReadoutLines + (title is null ? 1 : 2);
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

        // Every row is measured, shown or not, so the menu keeps one width as the window moves.
        float width = MathF.Max(
            MathF.Max(Font.Measure(_sceneName.Text).X, Font.Measure(_tick.Text).X),
            MathF.Max(PointerWidth, Font.Measure(_title.Text).X));
        width = MathF.Max(width, MathF.Max(Font.Measure(_status.Text).X, Font.Measure(_legend.Text).X));
        foreach (OverlayRow row in rows)
        {
            width = MathF.Max(width, Font.Measure(RowText(row)).X);
        }

        _width = (int)width + (Padding * 2);
        int lines = _rowsAbove + _shown + RowsBelowItems;
        _backdrop.Size = new Vector2(_width, (lines * Font.LineHeight) + (Padding * 2));
        _sceneName.Offset = new Vector2(Padding, RowTop(0));
        _tick.Offset = new Vector2(Padding, RowTop(1));
        _pointer.Offset = new Vector2(Padding, RowTop(2));
        _title.Offset = new Vector2(Padding, RowTop(ReadoutLines));
        _status.Offset = new Vector2(Padding, RowTop(lines - 2));
        _legend.Offset = new Vector2(Padding, RowTop(lines - 1));

        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index].Offset = new Vector2(Padding, RowTop(_rowsAbove + index));
            if (index >= _shown)
            {
                _rows[index].SetText(default);
            }
        }

        // Shown only past the window. The thumb is the window's share of the page.
        bool scrolls = rows.Count > MaxRows;
        float height = MaxRows * Font.LineHeight;
        _track.Offset = new Vector2(_width - Padding + ((Padding - ScrollbarWidth) / 2f), RowTop(_rowsAbove));
        _track.Size = scrolls ? new Vector2(ScrollbarWidth, height) : Vector2.Zero;
        _thumb.Size = scrolls ? new Vector2(ScrollbarWidth, height * _shown / rows.Count) : Vector2.Zero;

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
        _highlight.Offset = new Vector2(0f, RowTop(_rowsAbove + Math.Max(focusedRow, 0)));
        _highlight.Size = focused ? new Vector2(_width, Font.LineHeight) : Vector2.Zero;

        if (_page.Count > MaxRows)
        {
            float height = MaxRows * Font.LineHeight;
            _thumb.Offset = _track.Offset + new Vector2(0f, height * top / _page.Count);
        }
    }

    // Returns -1 off the rows. The menu hangs top-left, which makes a canvas position a menu one.
    internal int RowAt(Vector2 pointer)
    {
        if (!_menuShown || pointer.X < 0f || pointer.X > _width)
        {
            return -1;
        }

        float top = RowTop(_rowsAbove);
        int row = (int)MathF.Floor((pointer.Y - top) / Font.LineHeight);

        return pointer.Y >= top && row < _shown ? _top + row : -1;
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

    private static float RowTop(int row) => Padding + (row * Font.LineHeight);

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
