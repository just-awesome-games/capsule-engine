using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace MinimalGame.Game.UI;

/// <summary>
/// One item of a menu: a focus box, its caption and its highlight bar, all filling the item. It reacts to
/// its own focus and says it was pressed, never what pressing means. Everything is attached in its
/// constructor so the font is collected for the scene's preload.
/// </summary>
public sealed class MenuItem : ScreenEntity
{
    /// <summary>Canvas pixels between the centres of neighbouring items in a column.</summary>
    public const float Spacing = 20f;

    /// <summary>The box's narrowest width in canvas pixels, whatever the caption measures.</summary>
    public const float MinWidth = 88f;

    /// <summary>The box's height in canvas pixels.</summary>
    public const float BoxHeight = 16f;

    private const float HorizontalPadding = 8f;

    private static readonly ColorRgba FocusedInk = ColorRgba.Black;
    private static readonly ColorRgba RestingInk = ColorRgba.White;

    private readonly Label _caption;
    private readonly ColorRect _bar;

    /// <param name="anchor">Where the item sits in its menu's rect.</param>
    /// <param name="offset">Canvas pixels from that point to the same point of the item's box.</param>
    /// <param name="text">The caption drawn inside the box.</param>
    public MenuItem(Anchor anchor, Vector2 offset, string text)
        : base(anchor, offset)
    {
        _bar = new ColorRect { Visible = false };

        _caption = new Label(CapsuleAssets.Fonts.MenuFont, text)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Middle,
            Color = RestingInk,
        };

        Focusable = new Focusable();
        Focusable.Focused += OnFocused;
        Focusable.Unfocused += OnUnfocused;

        // The bar is attached first, so the caption draws over it.
        Add(_bar);
        Add(_caption);
        Add(Focusable);

        Fit(text);
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
            Fit(value);
        }
    }

    // Handed to the menu's navigator, which is the only thing that may move this item's focus.
    internal Focusable Focusable { get; }

    private void OnFocused()
    {
        _caption.Color = FocusedInk;
        _bar.Visible = true;
    }

    private void OnUnfocused()
    {
        _caption.Color = RestingInk;
        _bar.Visible = false;
    }

    // The box grows with the caption, so focused ink never runs off the bar.
    private void Fit(string text) =>
        Size = new Vector2(Math.Max(MinWidth, CapsuleAssets.Fonts.MenuFont.Measure(text).X + (2f * HorizontalPadding)), BoxHeight);
}
