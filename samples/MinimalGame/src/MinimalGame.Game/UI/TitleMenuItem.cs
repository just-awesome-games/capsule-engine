using System.Numerics;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;

namespace MinimalGame.Game.UI;

/// <summary>
/// One item of the title menu, and the shape an interface element takes in Capsule: a
/// <see cref="ScreenEntity"/> holding a <see cref="Focusable"/> for the box that can be focused, and
/// the renderers that show it. It owns its own reaction to the focus — the ink and the highlight bar
/// are its business, not its menu's — and it says it was pressed, never what pressing it means.
/// <para>
/// Everything it holds is attached in the constructor, so the font is collected for preload before
/// the scene starts.
/// </para>
/// </summary>
public sealed class TitleMenuItem : ScreenEntity
{
    // Wider and taller than either caption, so the hit target is the box rather than the glyphs alone.
    private static readonly Vector2 ItemBox = new(88f, 16f);

    private static readonly ColorRgba FocusedInk = ColorRgba.Black;
    private static readonly ColorRgba RestingInk = ColorRgba.White;

    private readonly Label _caption;
    private readonly ColorRect _bar;

    /// <param name="anchor">The point on the canvas <paramref name="offset"/> is measured from.</param>
    /// <param name="offset">Canvas pixels from that point to this item's centre.</param>
    /// <param name="text">The caption drawn inside the box.</param>
    public TitleMenuItem(Anchor anchor, Vector2 offset, string text)
        : base(anchor, offset)
    {
        // The box is centred on the entity, because the label's alignment point is its centre.
        Vector2 corner = -ItemBox / 2f;

        // Hidden by a zero extent rather than a transparent colour: the bar's colour then stays one
        // constant, and the size is what the focus already changes.
        _bar = new ColorRect(Vector2.Zero) { Offset = corner, ZIndex = -1 };

        _caption = new Label(CapsuleAssets.Fonts.Menu, text)
        {
            Size = ItemBox,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Middle,
            Color = RestingInk,
        };

        Focusable = new Focusable(ItemBox) { Offset = corner };
        Focusable.Focused += OnFocused;
        Focusable.Unfocused += OnUnfocused;

        Add(_bar);
        Add(_caption);
        Add(Focusable);
    }

    /// <summary>Raised when this item is pressed, however the player pressed it.</summary>
    public event Action? Pressed
    {
        add => Focusable.Pressed += value;
        remove => Focusable.Pressed -= value;
    }

    // Handed to the menu's navigator, which is the only thing that may move this item's focus.
    internal Focusable Focusable { get; }

    private void OnFocused()
    {
        _caption.Color = FocusedInk;
        _bar.Size = ItemBox;
    }

    private void OnUnfocused()
    {
        _caption.Color = RestingInk;
        _bar.Size = Vector2.Zero;
    }
}
