using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// One row of the debug panel: its text, the highlight bar it shows while focused, and the box the
// navigator focuses. It says it was pressed, never what pressing it means. The scene places it and
// sets its width.
internal sealed class DebugMenuRow : ScreenEntity
{
    private static readonly ColorRgba HighlightColor = new(255, 255, 255, 64);

    private readonly Label _label;
    private readonly ColorRect _highlight;

    internal DebugMenuRow(string text, float inset)
        : base(Anchor.TopLeft, Vector2.Zero)
    {
        // The bar and the box start at the panel's left edge, `inset` to the left of the text.
        Vector2 corner = new(-inset, 0f);

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

    internal string Text => _label.Text;

    internal float Width
    {
        set
        {
            Focusable.Size = new Vector2(value, BitmapFont.Default.LineHeight);

            if (Focusable.IsFocused)
            {
                _highlight.Size = Focusable.Size;
            }
        }
    }
}
