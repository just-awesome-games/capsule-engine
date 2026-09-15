using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// The screen entity drawing one menu item — its text, the highlight it shows while focused and
// the box the navigator focuses — which says it was pressed and never what pressing means.
internal sealed class MenuRow : ScreenEntity
{
    private static readonly ColorRgba HighlightColor = ColorRgba.White with { A = 64 };

    private readonly Label _label;
    private readonly ColorRect _highlight;
    private readonly string _text;
    private readonly Vector2 _corner;
    private float _width;
    private bool _shown = true;

    internal MenuRow(string text, float inset)
        : base(Anchor.TopLeft, Vector2.Zero)
    {
        _text = text;

        // The bar and the box start at the panel's left edge, `inset` to the left of the text.
        Vector2 corner = new(-inset, 0f);
        _corner = corner;

        // Hidden by a zero extent rather than a transparent colour: the size is what the focus
        // already changes.
        _highlight = new ColorRect(Vector2.Zero) { Color = HighlightColor, Offset = corner };
        _label = new Label(BitmapFont.Default, text);
        Focusable = new Focusable(new Vector2(0f, BitmapFont.Default.LineHeight)) { Offset = corner };
        Focusable.Focused += () => _highlight.Size = Focusable.Size;
        Focusable.Unfocused += () => _highlight.Size = Vector2.Zero;

        Add(_highlight);
        Add(_label);
        Add(Focusable);
    }

    internal event Action? Pressed
    {
        add => Focusable.Pressed += value;
        remove => Focusable.Pressed -= value;
    }

    // Handed to the scene's navigator, which is the only thing that may move this row's focus.
    internal Focusable Focusable { get; }

    // The row's text, whether or not it is shown.
    internal string Text => _text;

    internal float Width
    {
        set
        {
            _width = value;
            Apply();
        }
    }

    // Whether the row is in the menu's window. Hidden, the label is blank and the box and the
    // highlight have no extent, so the pointer never finds it; shown again, all three come back,
    // the highlight only if focused. Its position still serves the navigator's direction geometry.
    internal bool Shown
    {
        get => _shown;
        set
        {
            if (_shown == value)
            {
                return;
            }

            _shown = value;
            Apply();
        }
    }

    // A hidden box collapses onto the centre the shown box would have, so the navigator's
    // geometry still sees one straight column.
    private void Apply()
    {
        Vector2 size = new(_width, BitmapFont.Default.LineHeight);
        _label.SetText(_shown ? _text : string.Empty);
        Focusable.Size = _shown ? size : Vector2.Zero;
        Focusable.Offset = _shown ? _corner : _corner + (size / 2f);
        _highlight.Size = Focusable.IsFocused ? Focusable.Size : Vector2.Zero;
    }
}
