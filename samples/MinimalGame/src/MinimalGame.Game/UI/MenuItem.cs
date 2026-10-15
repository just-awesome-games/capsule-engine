using System.Numerics;
using Capsule;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace MinimalGame.Game.UI;

/// <summary>
/// One item of a menu: a focus box, its caption and its highlight bar, all filling the item, and an
/// optional <see cref="ActionPrompt"/> after the caption. It spans its slot's width, so a column's items
/// are all as wide as the widest. It reacts to its own focus and says it was pressed, never what pressing
/// means. Everything is attached in its constructor so the font and the prompt art are collected for the
/// scene's preload.
/// </summary>
public sealed class MenuItem : ScreenEntity
{
    /// <summary>Canvas pixels between neighbouring items in a column.</summary>
    public const float Spacing = 4f;

    /// <summary>The box's narrowest width in canvas pixels, whatever the caption measures.</summary>
    public const float MinWidth = 88f;

    /// <summary>The box's height in canvas pixels.</summary>
    public const float BoxHeight = 16f;

    private const float HorizontalPadding = 8f;

    // Canvas pixels between the caption and its prompt.
    private const float PromptGap = 4f;

    internal static readonly ColorRgba FocusedInk = ColorRgba.Black;
    internal static readonly ColorRgba RestingInk = ColorRgba.White;

    private readonly Label _caption;
    private readonly ColorRect _bar;
    private readonly ActionPrompt? _prompt;

    /// <param name="text">The caption drawn inside the box.</param>
    /// <param name="prompt">The action whose button is shown after the caption, or null for none.</param>
    public MenuItem(string text, InputAction? prompt = null)
        : base(Anchor.TopWide, Vector2.Zero)
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

        if (prompt is { } action)
        {
            _prompt = new ActionPrompt(this, action);
        }

        Size = new Vector2(WidthFor(text), BoxHeight);
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
        set => _caption.Text = value;
    }

    /// <summary>Whether the prompt after the caption shows. An item built with no prompt shows none.</summary>
    public bool ShowsPrompt
    {
        get => _prompt is { Visible: true };
        set => _prompt?.Visible = value;
    }

    // Handed to the menu's navigator, which is the only thing that may move this item's focus.
    internal Focusable Focusable { get; }

    private void OnFocused()
    {
        _caption.Color = FocusedInk;
        _prompt?.NameColor = FocusedInk;
        _bar.Visible = true;
    }

    private void OnUnfocused()
    {
        _caption.Color = RestingInk;
        _prompt?.NameColor = RestingInk;
        _bar.Visible = false;
    }

    /// <summary>The width in canvas pixels an item captioned <paramref name="text"/> with no prompt takes.</summary>
    public static float WidthFor(string text) =>
        Math.Max(MinWidth, CapsuleAssets.Fonts.MenuFont.Measure(text).X + (2f * HorizontalPadding));

    // The caption and its prompt are centred together, and the box grows to hold both so focused ink never
    // runs off the bar. The prompt has stepped by now, so its width is this step's.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        float caption = _caption.ContentSize.X;
        float trail = _prompt is { Visible: true, Width: > 0f } shown ? PromptGap + shown.Width : 0f;
        float width = Math.Max(MinWidth, caption + trail + (2f * HorizontalPadding));
        if (Size.X != width)
        {
            Size = new Vector2(width, BoxHeight);
        }

        if (_prompt is null)
        {
            return;
        }

        // The caption fills the item, so its bounds are the slot the item was given, which may be wider.
        float slot = _caption.Bounds.Size.X;
        _caption.Offset = new Vector2(-trail / 2f, 0f);
        _prompt.Position = new Vector2(((slot + caption - trail) / 2f) + PromptGap, (BoxHeight - ActionPrompt.Height) / 2f);
    }
}
