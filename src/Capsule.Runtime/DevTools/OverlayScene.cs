using System.Globalization;
using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// The scene that draws the overlay: a panel of row labels with a highlight on the focused row, a
// readout, a status line, a legend and the frame pane. It is told what to show once per overlay frame
// and keeps only the pixels between frames.
internal sealed class OverlayScene : Scene
{
    // Rows shown at once. A longer page is windowed to the rows around the focus.
    internal const int MaxRows = 24;

    // Panel pixels between the backdrop's edge and the text inside it, on every side.
    private const int Padding = 4;

    // Spaces between the widest label of a page and its hotkey column.
    private const int HotkeyGap = 2;

    // Rows under the last one: blank, status, legend.
    private const int RowsBelowItems = 3;

    private static readonly BitmapFont Font = BitmapFont.Default;

    private readonly ScreenEntity _panel;
    private readonly ColorRect _backdrop;
    private readonly ColorRect _highlight;
    private readonly Label _readout;
    private readonly Label _title;
    private readonly Label _status;
    private readonly Label _legend;
    private readonly Label[] _rows = new Label[MaxRows];

    // One row's text as drawn: its label padded to the hotkey column, then the key's name.
    private readonly char[] _text = new char[256];

    // The window's first row and how many are shown, as the last Show laid them out. These turn a
    // pointer position into a row index.
    private int _first;
    private int _shown;
    private int _rowsAbove;
    private int _width;

    private bool _menuShown = true;
    private bool _paneShown;

    internal OverlayScene(string toggleName)
    {
        ArgumentNullException.ThrowIfNull(toggleName);

        Sampling = TextureSampling.Point;

        _panel = new ScreenEntity(Anchor.TopLeft, Vector2.Zero);
        _backdrop = new ColorRect(Vector2.Zero) { Color = ColorRgba.Black with { A = 160 } };
        _highlight = new ColorRect(Vector2.Zero) { Color = ColorRgba.White with { A = 64 } };
        _readout = new Label(Font);
        _title = new Label(Font);
        _status = new Label(Font);
        _legend = new Label(Font, $"[{toggleName}] close   [Up/Dn] move   [Enter] select   [Bksp/Left] back");

        _panel.Add(_backdrop);
        _panel.Add(_highlight);
        _panel.Add(_readout);
        _panel.Add(_title);

        // The pool is added once, before the first frame. A label added later would wait for the step
        // that settles it, and the overlay draws rows on frames that run no step.
        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index] = new Label(Font);
            _panel.Add(_rows[index]);
        }

        _panel.Add(_status);
        _panel.Add(_legend);
        Add(_panel);
    }

    internal FramePane Pane { get; } = new();

    internal string Readout => _readout.Text;

    internal string Status => _status.Text;

    // The rows as drawn, top to bottom, hotkey column and all.
    internal string[] ShownRows()
    {
        string[] shown = new string[_shown];
        for (int index = 0; index < shown.Length; index++)
        {
            shown[index] = _rows[index].Text;
        }

        return shown;
    }

    // The focused row's highlight, empty while no row is focused.
    internal Rect Highlight => _highlight.Bounds;

    // Withdraws the panel from the scene, or brings it back. Returns whether anything changed.
    internal bool ShowMenu(bool shown)
    {
        if (_menuShown == shown)
        {
            return false;
        }

        _menuShown = shown;
        if (shown)
        {
            Add(_panel);
        }
        else
        {
            Remove(_panel);
            _shown = 0;
        }

        return true;
    }

    // Puts the pane in the scene or takes it out. The menu's own switch does not touch it.
    internal void ShowFramePane(bool shown)
    {
        if (_paneShown == shown)
        {
            return;
        }

        _paneShown = shown;
        if (shown)
        {
            Add(Pane);
        }
        else
        {
            Remove(Pane);
        }
    }

    internal void SetReadout(string sceneName, long tick)
    {
        _readout.Text = sceneName.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"tick {tick}")
            : string.Create(CultureInfo.InvariantCulture, $"{sceneName}  tick {tick}");
    }

    // The status row is one line, so it shows the first line of text.
    internal void SetStatus(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int end = text.AsSpan().IndexOfAny('\r', '\n');
        _status.Text = end < 0 ? text : text[..end];
    }

    // Lays the page out: the rows of the window around focus, the highlight on the focused one, and the
    // backdrop sized to the widest line. Does nothing while the panel is withdrawn.
    internal void Show(string? title, IReadOnlyList<OverlayRow> rows, int focus, int first)
    {
        if (!_menuShown)
        {
            return;
        }

        _title.Text = title ?? string.Empty;
        _rowsAbove = title is null ? 2 : 3;
        _first = Math.Clamp(first, 0, Math.Max(0, rows.Count - MaxRows));
        _shown = Math.Min(rows.Count, MaxRows);

        int column = 0;
        foreach (OverlayRow row in rows)
        {
            column = Math.Max(column, row.Label.Length);
        }

        column += HotkeyGap;

        float width = Widest(_readout.Text, _title.Text, _status.Text, _legend.Text);
        for (int index = 0; index < _shown; index++)
        {
            ReadOnlySpan<char> text = RowText(rows[_first + index], column);
            _rows[index].SetText(text);
            _rows[index].Offset = new Vector2(Padding, RowTop(_rowsAbove + index));
            width = MathF.Max(width, Font.Measure(text).X);
        }

        for (int index = _shown; index < _rows.Length; index++)
        {
            _rows[index].SetText(default);
        }

        // Every row of the page is measured, shown or not, so the panel keeps one width as the window
        // moves.
        for (int index = 0; index < rows.Count; index++)
        {
            if (index < _first || index >= _first + MaxRows)
            {
                width = MathF.Max(width, Font.Measure(RowText(rows[index], column)).X);
            }
        }

        _width = (int)width + (Padding * 2);
        int lines = _rowsAbove + _shown + RowsBelowItems;
        _backdrop.Size = new Vector2(_width, (lines * Font.LineHeight) + (Padding * 2));
        _readout.Offset = new Vector2(Padding, RowTop(0));
        _title.Offset = new Vector2(Padding, RowTop(1));

        int focusedRow = focus - _first;
        bool focused = focusedRow >= 0 && focusedRow < _shown && rows[focus].Activate is not null;
        _highlight.Offset = new Vector2(0f, RowTop(_rowsAbove + Math.Max(focusedRow, 0)));
        _highlight.Size = focused ? new Vector2(_width, Font.LineHeight) : Vector2.Zero;

        int statusRow = _rowsAbove + _shown + 1;
        _status.Offset = new Vector2(Padding, RowTop(statusRow));
        _legend.Offset = new Vector2(Padding, RowTop(statusRow + 1));
    }

    // The page row a canvas position is over, or -1 for a position off the rows. The panel hangs from
    // the canvas's top-left corner, and a canvas position is also a panel position.
    internal int RowAt(Vector2 pointer)
    {
        if (!_menuShown || pointer.X < 0f || pointer.X > _width)
        {
            return -1;
        }

        float top = RowTop(_rowsAbove);
        int row = (int)MathF.Floor((pointer.Y - top) / Font.LineHeight);

        return pointer.Y >= top && row < _shown ? _first + row : -1;
    }

    private ReadOnlySpan<char> RowText(in OverlayRow row, int column)
    {
        if (row.Hotkey is not { } hotkey)
        {
            return row.Label;
        }

        Span<char> text = _text;
        int length = Math.Min(row.Label.Length, text.Length);
        row.Label.AsSpan(0, length).CopyTo(text);
        for (; length < column && length < text.Length; length++)
        {
            text[length] = ' ';
        }

        string name = OverlayActions.KeyName(hotkey);
        int written = Math.Min(name.Length, text.Length - length);
        name.AsSpan(0, written).CopyTo(text[length..]);

        return text[..(length + written)];
    }

    private static float Widest(string a, string b, string c, string d) => MathF.Max(
        MathF.Max(Font.Measure(a).X, Font.Measure(b).X),
        MathF.Max(Font.Measure(c).X, Font.Measure(d).X));

    private static float RowTop(int row) => Padding + (row * Font.LineHeight);
}
