using System.Globalization;
using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// How a menu looks and navigates: one top-left panel holding the readout, the current menu's
// title, a row per item, a status line and the legend. Menus nest on a stack; the scene renders
// the one on top, reads the default menu's hotkeys at any depth, and every action it raises is one
// it was handed.
internal sealed class DebugScene : Scene
{
    private const int Padding = 4;

    // Spaces between the widest label of a menu and the hotkey column.
    private const int HotkeyGap = 2;

    // Overlay frames a held repeating hotkey waits before repeating, and the frames between repeats.
    private const int RepeatDelayFrames = 20;
    private const int RepeatIntervalFrames = 2;

    // Rows above the first item (readout, title) and below the last (blank, status, legend).
    private const int RowsAboveItems = 2;
    private const int RowsBelowItems = 3;

    private static readonly BitmapFont Font = BitmapFont.Default;

    private readonly ColorRect _backdrop;
    private readonly Label _readout;
    private readonly Label _title;
    private readonly Label _status;
    private readonly Label _legend;

    // The open menus, default first; the top is rendered.
    private readonly List<DebugMenu> _stack = [];
    private readonly FocusNavigator _navigator = new(DebugInput.MenuFocus);
    private DebugMenuRow[] _rows = [];

    private string? _readoutScene;
    private long _readoutTick;
    private int _heldFrames;

    internal DebugScene(string toggleName)
    {
        ArgumentNullException.ThrowIfNull(toggleName);

        Sampling = TextureSampling.Point;

        ScreenEntity panel = new(Anchor.TopLeft, Vector2.Zero);
        _backdrop = new ColorRect(Vector2.Zero) { Color = ColorRgba.Black with { A = 160 } };
        _readout = new Label(Font);
        _title = new Label(Font);
        _status = new Label(Font);
        _legend = new Label(Font, $"[{toggleName}] close   [Up/Dn] move   [Enter] select   [Bksp/Left] back");

        panel.Add(_backdrop);
        panel.Add(_readout);
        panel.Add(_title);
        panel.Add(_status);
        panel.Add(_legend);
        panel.Add(_navigator);
        Add(panel);

        _navigator.FocusChanged += Remember;
    }

    internal DebugMenu Menu => _stack[^1];

    internal int Depth => _stack.Count;

    internal int FocusedIndex => Menu.Focus;

    internal string Readout => _readout.Text;

    internal string Title => _title.Text;

    internal string Status => _status.Text;

    internal string RowText(int index) => _rows[index].Text;

    // Makes menu current, focused where it remembers; the menu already shown stays as it is, so a
    // hotkey pressed inside its own submenu does not stack it twice.
    internal void Push(DebugMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        if (_stack.Count > 0 && ReferenceEquals(Menu, menu))
        {
            return;
        }

        _stack.Add(menu);
        Show(menu);
    }

    // Swaps the current menu for one rebuilt in place — its rows changed under it — keeping the
    // depth and the focus.
    internal void Replace(DebugMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        menu.Focus = Math.Min(Menu.Focus, menu.Items.Count - 1);
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
        Show(Menu);
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

        if (input.WasPressed(DebugInput.Back))
        {
            Pop();
        }

        // The default menu's hotkeys work at any depth. A press activates at once; a repeating item
        // held past the delay activates again every interval. One counter serves them all,
        // advanced once per step: it runs while any repeating hotkey is held and restarts on any
        // press edge.
        IReadOnlyList<DebugMenuItem> items = _stack[0].Items;
        bool anyPressed = false;
        bool anyHeld = false;
        for (int index = 0; index < items.Count; index++)
        {
            DebugMenuItem item = items[index];
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
            DebugMenuItem item = items[index];
            if (item.Hotkey is { } hotkey
                && (input.WasPressed(hotkey) || (repeat && item.Repeats && input.IsHeld(hotkey))))
            {
                Activate(item);
            }
        }
    }

    private void Activate(DebugMenuItem item)
    {
        SetStatus(string.Empty);
        item.Activate();
    }

    // Replaces the rows with menu's, one per item, and asks the navigator for the one the menu
    // remembers. A hotkeyed row is its label padded to a shared column, then the key's name; the
    // font is monospace.
    private void Show(DebugMenu menu)
    {
        // Out of the scene before out of the navigator, so the focus is released once rather than
        // repaired onto each row about to leave; `_rows` is cleared first so the release is not
        // remembered against the menu being shown.
        DebugMenuRow[] leaving = _rows;
        _rows = [];
        foreach (DebugMenuRow row in leaving)
        {
            Remove(row);
        }

        foreach (DebugMenuRow row in leaving)
        {
            _navigator.Remove(row.Focusable);
        }

        IReadOnlyList<DebugMenuItem> items = menu.Items;
        int column = 0;
        for (int index = 0; index < items.Count; index++)
        {
            column = Math.Max(column, items[index].Label.Length);
        }

        column += HotkeyGap;

        DebugMenuRow[] rows = new DebugMenuRow[items.Count];
        for (int index = 0; index < items.Count; index++)
        {
            DebugMenuItem item = items[index];
            string text = item.Hotkey is { } hotkey ? item.Label.PadRight(column) + DebugInput.KeyName(hotkey) : item.Label;
            DebugMenuRow row = new(text, Padding);
            row.Pressed += () => Activate(item);
            rows[index] = row;
            Add(row);
            _navigator.Add(row.Focusable);
        }

        _rows = rows;
        _navigator.Focus(rows[menu.Focus].Focusable);
        _title.Text = menu.Title ?? string.Empty;
        Layout();
    }

    // Writes the focused row's index to the current menu; a row that is not one of the current
    // rows is one on its way out.
    private void Remember(Focusable focused)
    {
        for (int index = 0; index < _rows.Length; index++)
        {
            if (ReferenceEquals(_rows[index].Focusable, focused))
            {
                Menu.Focus = index;

                return;
            }
        }
    }

    // Places every line from the top and sizes the backdrop to the widest.
    private void Layout()
    {
        float width = Font.Measure(_readout.Text).X;
        width = MathF.Max(width, Font.Measure(_title.Text).X);
        width = MathF.Max(width, Font.Measure(_status.Text).X);
        width = MathF.Max(width, Font.Measure(_legend.Text).X);
        foreach (DebugMenuRow row in _rows)
        {
            width = MathF.Max(width, Font.Measure(row.Text).X);
        }

        int panelWidth = (int)width + (Padding * 2);
        int rowCount = RowsAboveItems + _rows.Length + RowsBelowItems;

        _backdrop.Size = new Vector2(panelWidth, (rowCount * Font.LineHeight) + (Padding * 2));
        _readout.Offset = new Vector2(Padding, RowTop(0));
        _title.Offset = new Vector2(Padding, RowTop(1));

        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index].Teleport(new Vector2(Padding, RowTop(RowsAboveItems + index)));
            _rows[index].Width = panelWidth;
        }

        int statusRow = RowsAboveItems + _rows.Length + 1;
        _status.Offset = new Vector2(Padding, RowTop(statusRow));
        _legend.Offset = new Vector2(Padding, RowTop(statusRow + 1));
    }

    private static float RowTop(int row) => Padding + (row * Font.LineHeight);
}
