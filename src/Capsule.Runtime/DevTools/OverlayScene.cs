using System.Globalization;
using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// The scene that is the overlay: one panel of menu rows over a stack of menus, a status line, a
// legend and the frame pane, with how the rows navigate, window and scroll; nothing of what any
// action does, which is handed in.
internal sealed class OverlayScene : Scene
{
    private const int Padding = 4;

    // Rows a menu shows at once; a longer menu is windowed.
    internal const int MaxRows = 24;

    // Rows the window moves per wheel notch.
    private const int RowsPerNotch = 3;

    // Spaces between the widest label of a menu and the hotkey column.
    private const int HotkeyGap = 2;

    // The scrollbar's width in panel pixels, set inside the right padding.
    private const int ScrollbarWidth = 2;

    // Overlay frames a held repeating hotkey, or a held direction, waits before repeating, and the
    // frames between repeats.
    internal const int RepeatDelayFrames = 60;
    internal const int RepeatIntervalFrames = 10;

    // Rows below the last item (blank, status, legend). Above the first: the readout, the title
    // when the menu has one, and one blank — never two, so an untitled menu drops the title row.
    private const int RowsBelowItems = 3;

    private static readonly BitmapFont Font = BitmapFont.Default;
    private static readonly ColorRgba TrackColor = ColorRgba.White with { A = 48 };
    private static readonly ColorRgba ThumbColor = ColorRgba.White with { A = 160 };

    private readonly ScreenEntity _panel;
    private readonly ColorRect _backdrop;
    private readonly Label _readout;
    private readonly Label _title;
    private readonly Label _status;
    private readonly Label _legend;
    private readonly ColorRect _track;
    private readonly ColorRect _thumb;

    // The open menus, default first; the top is rendered.
    private readonly List<Menu> _stack = [];
    private readonly FocusNavigator _navigator = new(OverlayActions.MenuFocus)
    {
        RepeatDelay = RepeatDelayFrames,
        RepeatInterval = RepeatIntervalFrames,
    };

    private MenuRow[] _rows = [];

    private string? _readoutScene;
    private long _readoutTick;
    private int _heldFrames;

    // Wheel rows not yet applied: a fine wheel or a touchpad reports fractions of a notch, which
    // add up here until they make a row.
    private float _scrollRemainder;
    private bool _menuShown = true;
    private bool _paneShown;

    internal OverlayScene(string toggleName)
    {
        ArgumentNullException.ThrowIfNull(toggleName);

        Sampling = TextureSampling.Point;

        ScreenEntity panel = new(Anchor.TopLeft, Vector2.Zero);
        _backdrop = new ColorRect(Vector2.Zero) { Color = ColorRgba.Black with { A = 160 } };
        _readout = new Label(Font);
        _title = new Label(Font);
        _status = new Label(Font);
        _legend = new Label(Font, $"[{toggleName}] close   [Up/Dn] move   [Enter] select   [Bksp/Left] back");
        _track = new ColorRect(Vector2.Zero) { Color = TrackColor };
        _thumb = new ColorRect(Vector2.Zero) { Color = ThumbColor };

        panel.Add(_backdrop);
        panel.Add(_readout);
        panel.Add(_title);
        panel.Add(_status);
        panel.Add(_legend);
        panel.Add(_track);
        panel.Add(_thumb);
        panel.Add(_navigator);
        _panel = panel;
        Add(panel);

        _navigator.FocusChanged += Remember;
    }

    internal FramePane Pane { get; } = new();

    internal Menu Current => _stack[^1];

    internal int Depth => _stack.Count;

    // Whether menu is open at any depth, current or beneath.
    internal bool Contains(Menu menu) => _stack.Contains(menu);

    internal int FocusedIndex => Current.Focus;

    internal int RowCount => _rows.Length;

    internal int First => Current.First;

    internal bool IsRowShown(int index) => _rows[index].Shown;

    internal Rect RowBounds(int index) => _rows[index].Focusable.Bounds;

    internal string Readout => _readout.Text;

    internal string Title => _title.Text;

    internal string Status => _status.Text;

    internal string RowText(int index) => _rows[index].Text;

    // The scrollbar's track and thumb as drawn; each empty while the menu fits its window.
    internal Rect ScrollTrack => _track.Bounds;

    internal Rect ScrollThumb => _thumb.Bounds;

    // Withdraws the panel and its rows from the scene, or brings them back showing the current
    // menu; the stack and every menu's remembered focus are untouched either way. Returns whether
    // anything changed.
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
            if (_stack.Count > 0)
            {
                Show(Current);
            }
        }
        else
        {
            RemoveRows();
            Remove(_panel);
        }

        return true;
    }

    // Puts the pane in the scene or takes it out; it is never touched by the menu's switch.
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

    // Makes menu current, focused where it remembers; the menu already shown stays as it is, so a
    // hotkey pressed inside its own submenu does not stack it twice.
    internal void Push(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        if (_stack.Count > 0 && ReferenceEquals(Current, menu))
        {
            return;
        }

        _stack.Add(menu);
        Show(menu);
    }

    // Swaps the current menu for one rebuilt in place — its rows changed under it — keeping the
    // depth, the focus and the window.
    internal void Replace(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        menu.Focus = Interactive(menu, Math.Min(Current.Focus, menu.Items.Count - 1));
        menu.First = Current.First;
        _stack[^1] = menu;
        Show(menu);
    }

    // Returns to the menu beneath, focused on the item that opened this one; nothing on the last.
    internal void Pop()
    {
        if (_stack.Count < 2)
        {
            return;
        }

        _stack.RemoveAt(_stack.Count - 1);
        Show(Current);
    }

    internal void SetReadout(string sceneName, long tick)
    {
        ArgumentNullException.ThrowIfNull(sceneName);

        if (_readoutTick == tick && string.Equals(_readoutScene, sceneName, StringComparison.Ordinal))
        {
            return;
        }

        _readoutScene = sceneName;
        _readoutTick = tick;
        _readout.Text = string.Create(CultureInfo.InvariantCulture, $"{sceneName}  tick {tick}");
        Layout();
    }

    // The status row is one line: the first line of text is shown.
    internal void SetStatus(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int end = text.AsSpan().IndexOfAny('\r', '\n');
        string line = end < 0 ? text : text[..end];

        if (string.Equals(_status.Text, line, StringComparison.Ordinal))
        {
            return;
        }

        _status.Text = line;
        Layout();
    }

    protected override void OnStep(in StepContext context)
    {
        InputState input = context.Input;

        if (input.WasPressed(OverlayActions.Back))
        {
            Pop();
        }

        Scroll(input.Axis(OverlayActions.Scroll));

        // The default menu's hotkeys work at any depth. A press activates at once; a repeating item
        // held past the delay activates again every interval. One counter serves them all,
        // advanced once per step: it runs while any repeating hotkey is held and restarts on any
        // press edge.
        IReadOnlyList<MenuItem> items = _stack[0].Items;
        bool anyPressed = false;
        bool anyHeld = false;
        for (int index = 0; index < items.Count; index++)
        {
            MenuItem item = items[index];
            if (item.Hotkey is { } hotkey)
            {
                anyPressed |= input.WasPressed(hotkey);
                anyHeld |= item.Repeats && input.IsHeld(hotkey);
            }
        }

        _heldFrames = anyHeld && !anyPressed ? _heldFrames + 1 : 0;
        bool repeat = _heldFrames >= RepeatDelayFrames
            && (_heldFrames - RepeatDelayFrames) % RepeatIntervalFrames == 0;

        for (int index = 0; index < items.Count; index++)
        {
            MenuItem item = items[index];
            if (item.Hotkey is { } hotkey
                && (input.WasPressed(hotkey) || (repeat && item.Repeats && input.IsHeld(hotkey))))
            {
                Activate(item);
            }
        }
    }

    private void Activate(MenuItem item)
    {
        SetStatus(string.Empty);
        item.Activate?.Invoke();
    }

    // The wheel moves the window, three rows a notch, away from the user scrolling up; the focus
    // stays where it is, and the next direction press brings the window back to it. Whole rows
    // are applied and the fraction kept for the next step.
    private void Scroll(float notches)
    {
        if (notches == 0f || _stack.Count == 0 || _rows.Length <= MaxRows)
        {
            return;
        }

        _scrollRemainder -= notches * RowsPerNotch;
        int rows = (int)MathF.Truncate(_scrollRemainder);
        _scrollRemainder -= rows;

        Menu menu = Current;
        int first = Math.Clamp(menu.First + rows, 0, _rows.Length - MaxRows);
        if (first != menu.First)
        {
            menu.First = first;
            Layout();
        }
    }

    // The interactive item nearest index, searched outward by distance and, at a tie, the earlier
    // one: the reader came from above. Index itself where the menu has no interactive item.
    private static int Interactive(Menu menu, int index)
    {
        IReadOnlyList<MenuItem> items = menu.Items;
        for (int distance = 0; distance < items.Count; distance++)
        {
            int before = index - distance;
            if (before >= 0 && before < items.Count && items[before].Activate is not null)
            {
                return before;
            }

            int after = index + distance;
            if (after < items.Count && after >= 0 && items[after].Activate is not null)
            {
                return after;
            }
        }

        return index;
    }

    // Replaces the rows with menu's, one per item, and asks the navigator for the one the menu
    // remembers. A hotkeyed row is its label padded to a shared column, then the key's name; the
    // font is monospace. A row with no action is drawn and never focused: it is not the
    // navigator's. Nothing while the menu is withdrawn: the rows are built when it is shown.
    // Every row of a menu longer than MaxRows is still built and every interactive one is still
    // the navigator's, so the focus moves, repeats and wraps through the whole list as on a short
    // one; Layout hides the rows outside the window, the window follows the focus, and the wheel
    // scrolls it without moving the focus.
    private void Show(Menu menu)
    {
        if (!_menuShown)
        {
            return;
        }

        RemoveRows();

        IReadOnlyList<MenuItem> items = menu.Items;
        int column = 0;
        for (int index = 0; index < items.Count; index++)
        {
            column = Math.Max(column, items[index].Label.Length);
        }

        column += HotkeyGap;

        MenuRow[] rows = new MenuRow[items.Count];
        for (int index = 0; index < items.Count; index++)
        {
            MenuItem item = items[index];
            string text = item.Hotkey is { } hotkey ? item.Label.PadRight(column) + OverlayActions.KeyName(hotkey) : item.Label;
            MenuRow row = new(text, Padding);
            rows[index] = row;
            Add(row);
            if (item.Activate is not null)
            {
                row.Pressed += () => Activate(item);
                _navigator.Add(row.Focusable);
            }
        }

        _rows = rows;
        menu.Focus = Interactive(menu, menu.Focus);
        if (items[menu.Focus].Activate is not null)
        {
            _navigator.Focus(rows[menu.Focus].Focusable);
        }

        _title.Text = menu.Title ?? string.Empty;
        _scrollRemainder = 0f;
        Follow(menu);
        Layout();
    }

    // Moves the window the least that brings the focused item inside it, and pulls a window that
    // reads past the end back. A menu with nothing to focus has no item to follow, so its window
    // stays where the wheel left it.
    private static void Follow(Menu menu)
    {
        int first = Math.Clamp(menu.First, 0, Math.Max(0, menu.Items.Count - MaxRows));
        menu.First = menu.Items[menu.Focus].Activate is null
            ? first
            : Math.Clamp(first, menu.Focus - MaxRows + 1, menu.Focus);
    }

    // Out of the scene before out of the navigator, so the focus is released once rather than
    // repaired onto each row about to leave; `_rows` is cleared first so the release is not
    // remembered against the menu being shown.
    private void RemoveRows()
    {
        MenuRow[] leaving = _rows;
        _rows = [];
        foreach (MenuRow row in leaving)
        {
            Remove(row);
        }

        foreach (MenuRow row in leaving)
        {
            _navigator.Remove(row.Focusable);
        }
    }

    // Writes the focused row's index to the current menu and brings the window to it; a row that
    // is not one of the current rows is one on its way out.
    private void Remember(Focusable focused)
    {
        for (int index = 0; index < _rows.Length; index++)
        {
            if (ReferenceEquals(_rows[index].Focusable, focused))
            {
                Menu menu = Current;
                menu.Focus = index;
                int first = menu.First;
                Follow(menu);
                if (menu.First != first)
                {
                    Layout();
                }

                return;
            }
        }
    }

    // Places every line from the top and sizes the backdrop to the widest and to the window. Rows
    // are laid out contiguously from the window's first, so a hidden row sits above or below the
    // panel where the navigator's geometry still finds it in order.
    private void Layout()
    {
        float width = Font.Measure(_readout.Text).X;
        width = MathF.Max(width, Font.Measure(_title.Text).X);
        width = MathF.Max(width, Font.Measure(_status.Text).X);
        width = MathF.Max(width, Font.Measure(_legend.Text).X);
        foreach (MenuRow row in _rows)
        {
            width = MathF.Max(width, Font.Measure(row.Text).X);
        }

        int first = _stack.Count > 0 ? Current.First : 0;
        int shown = Math.Min(_rows.Length, MaxRows);
        int panelWidth = (int)width + (Padding * 2);
        int rowCount = RowsAboveItems + shown + RowsBelowItems;

        _backdrop.Size = new Vector2(panelWidth, (rowCount * Font.LineHeight) + (Padding * 2));
        _readout.Offset = new Vector2(Padding, RowTop(0));
        _title.Offset = new Vector2(Padding, RowTop(1));

        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index].Teleport(new Vector2(Padding, RowTop(RowsAboveItems + index - first)));
            _rows[index].Width = panelWidth;
            _rows[index].Shown = index >= first && index < first + MaxRows;
        }

        int statusRow = RowsAboveItems + shown + 1;
        _status.Offset = new Vector2(Padding, RowTop(statusRow));
        _legend.Offset = new Vector2(Padding, RowTop(statusRow + 1));

        // The scrollbar spans the row window; the thumb is the window's share of the list, at
        // least a row tall so it never vanishes, and sits where First is along the list.
        if (_rows.Length > MaxRows)
        {
            float top = RowTop(RowsAboveItems);
            float height = MaxRows * Font.LineHeight;
            float thumb = MathF.Max(Font.LineHeight, height * MaxRows / _rows.Length);
            float travel = (height - thumb) * first / (_rows.Length - MaxRows);
            float left = panelWidth - Padding + ((Padding - ScrollbarWidth) / 2f);
            _track.Offset = new Vector2(left, top);
            _track.Size = new Vector2(ScrollbarWidth, height);
            _thumb.Offset = new Vector2(left, top + travel);
            _thumb.Size = new Vector2(ScrollbarWidth, thumb);
        }
        else
        {
            _track.Size = Vector2.Zero;
            _thumb.Size = Vector2.Zero;
        }
    }

    private int RowsAboveItems => _stack.Count > 0 && Current.Title is not null ? 3 : 2;

    private static float RowTop(int row) => Padding + (row * Font.LineHeight);
}
