namespace MinimalGame.Game.UI;

/// <summary>One row of a menu holding a level from 0 to 1 in tenths.</summary>
/// <remarks>
/// It draws its caption, a <see cref="Meter"/> showing the level, and the same highlight a
/// <see cref="MenuItem"/> takes. It spans its slot's width and its meter sits on its right edge. A
/// column's meters then line up. Left and right step the level while it has the focus, and up and down
/// move on. Dragging the meter or turning the wheel over the focused row sets the level too.
/// </remarks>
public sealed class VolumeSlider : ScreenEntity
{
    private const float HorizontalPadding = 8f;
    private const float Gap = 8f;

    private readonly Label _caption;
    private readonly ColorRect _bar;
    private readonly Meter _meter = new(Anchor.Right, Vector2.Zero);

    /// <summary>Raised with the new level when the player changes it, and never when <see cref="Value"/> is set.</summary>
    public event Action<float>? Changed;

    /// <summary>The level from 0 to 1, full until something sets it. Setting it raises nothing.</summary>
    public float Value
    {
        get => _meter.Fraction;
        set => _meter.Fraction = value;
    }

    /// <param name="text">The caption drawn before the meter.</param>
    public VolumeSlider(TextKey text)
        : base(Anchor.TopWide, Vector2.Zero)
    {
        Padding = new Insets(HorizontalPadding, 0f, HorizontalPadding, 0f);

        _bar = new ColorRect { Visible = false };

        // Components fill the outer rect. The caption is moved in by the padding itself.
        _caption = new Label(CapsuleAssets.Fonts.MenuFont, text)
        {
            Offset = new Vector2(HorizontalPadding, 0f),
            VerticalAlignment = VerticalAlignment.Middle,
            Color = MenuItem.RestingInk,
        };

        _meter.Parent = this;

        // The menu's navigator gathers this row, and nothing else needs to hold it.
        Focusable focus = new() { Adjusts = Axis.Horizontal };
        MenuItem.Highlight(focus, _caption, _bar);
        focus.Adjusted += step => Set(Value + (step / 10f));
        focus.Dragged += drag =>
        {
            // A press on the caption focuses the row and leaves the level where it was.
            if (_meter.Contains(drag.Start))
            {
                Set(_meter.FractionAt(drag.Position));
            }
        };

        Add(_bar);
        Add(_caption);
        Add(focus);
    }

    /// <summary>The narrowest width in canvas pixels that holds the caption, the gap and the meter inside the padding.</summary>
    public float ContentWidth => _caption.ContentSize.X + Gap + _meter.Size.X + (2f * HorizontalPadding);

    // Sized from the caption in the run's language, and again whenever the language changes it.
    /// <inheritdoc/>
    protected override void OnStart() => Size = new Vector2(ContentWidth, MenuItem.BoxHeight);

    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        if (Size.X != ContentWidth)
        {
            Size = new Vector2(ContentWidth, MenuItem.BoxHeight);
        }
    }

    // Rounded to tenths, where a drag and the arrows land on the same levels.
    private void Set(float level)
    {
        float stepped = Math.Clamp(MathF.Round(level * 10f) / 10f, 0f, 1f);

        if (stepped == Value)
        {
            return;
        }

        Value = stepped;
        Changed?.Invoke(stepped);
    }
}
