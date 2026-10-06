using System.Numerics;
using Capsule.Rendering;
using Capsule.UI;

namespace MinimalGame.Game.UI;

/// <summary>One row of a menu holding a level from 0 to 1 in tenths.</summary>
/// <remarks>
/// It draws its caption, a <see cref="Meter"/> showing the level, and the same highlight a
/// <see cref="MenuItem"/> takes. It spans its slot's width and its meter sits on its right edge. A
/// column's meters then line up. Left and right step the level while it has the focus, and up and down
/// move on.
/// </remarks>
public sealed class VolumeSlider : ScreenEntity
{
    private const float HorizontalPadding = 8f;
    private const float Gap = 8f;

    private readonly Label _caption;
    private readonly ColorRect _bar;
    private readonly Meter _meter = new(Anchor.Right, Vector2.Zero);

    /// <summary>Raised with the new level when the player steps it, and never when <see cref="Value"/> is set.</summary>
    public event Action<float>? Changed;

    /// <summary>The level from 0 to 1, full until something sets it. Setting it raises nothing.</summary>
    public float Value
    {
        get => _meter.Fraction;
        set => _meter.Fraction = value;
    }

    /// <param name="text">The caption drawn before the meter.</param>
    public VolumeSlider(string text)
        : base(Anchor.TopWide, Vector2.Zero)
    {
        // The narrowest row that holds the caption, the gap and the meter inside the padding.
        float width = CapsuleAssets.Fonts.MenuFont.Measure(text).X + Gap + _meter.Size.X + (2f * HorizontalPadding);
        Size = new Vector2(width, MenuItem.BoxHeight);
        Padding = new Insets(HorizontalPadding, 0f, HorizontalPadding, 0f);

        _bar = new ColorRect { Visible = false };

        // Components fill the outer rect, so the caption is moved in by the padding itself.
        _caption = new Label(CapsuleAssets.Fonts.MenuFont, text)
        {
            Offset = new Vector2(HorizontalPadding, 0f),
            VerticalAlignment = VerticalAlignment.Middle,
            Color = MenuItem.RestingInk,
        };

        _meter.Parent = this;

        // The menu's navigator gathers this row, so nothing else needs to hold it.
        Focusable focus = new() { Adjusts = Axis.Horizontal };
        focus.Focused += OnFocused;
        focus.Unfocused += OnUnfocused;
        focus.Adjusted += Step;

        // The bar is attached first, so the caption draws over it.
        Add(_bar);
        Add(_caption);
        Add(focus);
    }

    // Rounded to tenths, so stepping down and back up lands on the level it left.
    private void Step(int direction)
    {
        float stepped = Math.Clamp(MathF.Round((Value * 10f) + direction) / 10f, 0f, 1f);

        if (stepped == Value)
        {
            return;
        }

        Value = stepped;
        Changed?.Invoke(stepped);
    }

    private void OnFocused()
    {
        _caption.Color = MenuItem.FocusedInk;
        _bar.Visible = true;
    }

    private void OnUnfocused()
    {
        _caption.Color = MenuItem.RestingInk;
        _bar.Visible = false;
    }
}
