using System.Numerics;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace MinimalGame.Game.UI;

/// <summary>
/// One item of a menu: a focusable box, its caption and its highlight bar. The ink and the bar
/// are this item's own reaction to the focus, and it says it was pressed, never what pressing it means.
/// Everything it holds is attached in the constructor, so the font is collected for the scene's
/// preload.
/// </summary>
public sealed class MenuItem : ScreenEntity
{
    // The box is at least this wide, whatever the caption measures.
    private const float MinWidth = 88f;
    private const float BoxHeight = 16f;
    private const float HorizontalPadding = 8f;

    private static readonly ColorRgba FocusedInk = ColorRgba.Black;
    private static readonly ColorRgba RestingInk = ColorRgba.White;

    private readonly Label _caption;
    private readonly ColorRect _bar;

    private Vector2 _box;
    private bool _focused;

    /// <param name="anchor">The point on the canvas <paramref name="offset"/> is measured from.</param>
    /// <param name="offset">Canvas pixels from that point to this item's centre.</param>
    /// <param name="text">The caption drawn inside the box.</param>
    public MenuItem(Anchor anchor, Vector2 offset, string text)
        : base(anchor, offset)
    {
        // Hidden by a zero extent rather than a transparent colour: the bar's colour then stays one
        // constant, and the size is what the focus already changes.
        _bar = new ColorRect(Vector2.Zero) { ZIndex = -1 };

        _caption = new Label(CapsuleAssets.Fonts.Menu, text)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Middle,
            Color = RestingInk,
        };

        Focusable = new Focusable(Vector2.Zero);
        Focusable.Focused += OnFocused;
        Focusable.Unfocused += OnUnfocused;

        Add(_bar);
        Add(_caption);
        Add(Focusable);

        Grow(text);
    }

    /// <summary>Raised when this item is pressed, however the player pressed it.</summary>
    public event Action? Pressed
    {
        add => Focusable.Pressed += value;
        remove => Focusable.Pressed -= value;
    }

    /// <summary>The label drawn inside the box.</summary>
    public string Caption
    {
        get => _caption.Text;
        set
        {
            _caption.Text = value;
            Grow(value);
        }
    }

    // Handed to the menu's navigator, which is the only thing that may move this item's focus.
    internal Focusable Focusable { get; }

    private void OnFocused()
    {
        _focused = true;
        _caption.Color = FocusedInk;
        _bar.Size = _box;
    }

    private void OnUnfocused()
    {
        _focused = false;
        _caption.Color = RestingInk;
        _bar.Size = Vector2.Zero;
    }

    // The box grows with the caption, so focused ink never runs off the bar.
    private void Grow(string text)
    {
        Vector2 measured = CapsuleAssets.Fonts.Menu.Measure(text);
        _box = new Vector2(Math.Max(MinWidth, measured.X + (2f * HorizontalPadding)), BoxHeight);

        // The bar, the caption and the focus box are one box centred on the entity, so all three hang
        // from the same corner.
        Vector2 corner = -_box / 2f;

        _bar.Offset = corner;
        _bar.Size = _focused ? _box : Vector2.Zero;

        _caption.Offset = corner;
        _caption.Size = _box;

        Focusable.Offset = corner;
        Focusable.Size = _box;
    }
}
