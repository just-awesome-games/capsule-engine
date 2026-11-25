namespace MinimalGame.Game.UI;

/// <summary>
/// One menu item as wide as its slot, holding a focus box, a caption, a highlight bar and an optional
/// <see cref="ActionPrompt"/>, which says it was pressed and never what pressing means.
/// </summary>
/// <remarks>
/// The item fits its caption and prompt as they show now, and a column of items is as wide as the widest.
/// </remarks>
public sealed class MenuItem : BoxContainer
{
    /// <summary>Canvas pixels between neighbouring items in a column.</summary>
    public const float RowSpacing = 4f;

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

    // The rect the prompt draws in, which fits it as it shows.
    private readonly ScreenEntity? _promptSlot;

    // Handed to the menu's navigator, which is the only thing that may move this item's focus.
    internal Focusable Focusable { get; }

    /// <param name="text">The caption drawn inside the box.</param>
    /// <param name="prompt">The action whose button is shown after the caption, or null for none.</param>
    public MenuItem(TextKey text, InputAction? prompt = null)
        : this(prompt) => _caption.SetText(text);

    /// <summary>An item whose caption is set once it starts, as a row showing a setting's value is.</summary>
    /// <param name="prompt">The action whose button is shown after the caption, or null for none.</param>
    public MenuItem(InputAction? prompt = null)
        : base(Axis.Vertical, Anchor.TopWide, Vector2.Zero)
    {
        // Everything is attached here, where the font and the prompt art join the scene's preload.
        // Spanning the slot makes a column's items all as wide as the widest.
        Size = new Vector2(MinWidth, BoxHeight);
        Padding = new Insets(HorizontalPadding, 0f, HorizontalPadding, 0f);

        _bar = new ColorRect { Visible = false };
        _caption = new Label(CapsuleAssets.Fonts.MenuFont) { Color = RestingInk };
        Focusable = new Focusable();
        Add(_bar);
        Add(Focusable);

        // The caption and the prompt sit side by side, centred together in the item.
        BoxContainer line = new(Axis.Horizontal, Anchor.Top, Vector2.Zero) { Parent = this, Size = new Vector2(0f, BoxHeight), Spacing = PromptGap };
        new ScreenEntity(Anchor.Left, Vector2.Zero) { Parent = line }.Add(_caption);

        if (prompt is { } action)
        {
            _promptSlot = new ScreenEntity(Anchor.Left, Vector2.Zero) { Parent = line };
            _prompt = new ActionPrompt(_promptSlot, action);
        }

        Highlight(Focusable, _caption, _bar, _prompt);
    }

    /// <summary>Raised when this item is pressed, however the player pressed it.</summary>
    public event Action? Pressed
    {
        add => Focusable.Pressed += value;
        remove => Focusable.Pressed -= value;
    }

    /// <summary>Raised with the action when one of the menu's secondary actions presses this item.</summary>
    public event Action<InputAction>? SecondaryPressed
    {
        add => Focusable.SecondaryPressed += value;
        remove => Focusable.SecondaryPressed -= value;
    }

    /// <summary>The caption as it shows now, in the run's language.</summary>
    public string Caption => _caption.Text;

    /// <summary>Shows <paramref name="text"/> inside the box.</summary>
    public void SetCaption(TextKey text) => _caption.SetText(text);

    /// <summary>Shows <paramref name="text"/> inside the box with its placeholder filled by <paramref name="value"/>.</summary>
    public void SetCaption<T>(TextKey text, T value) => _caption.SetText(text, value);

    /// <summary>Whether the prompt after the caption shows. An item built with no prompt shows none.</summary>
    public bool ShowsPrompt
    {
        get => _promptSlot is { Visible: true };
        set => _promptSlot?.Visible = value;
    }

    // Inks a row's caption, and its prompt when it has one, and shows its bar while the row holds the focus.
    internal static void Highlight(Focusable focus, Label caption, ColorRect bar, ActionPrompt? prompt = null)
    {
        focus.Focused += () => Ink(FocusedInk, focused: true);
        focus.Unfocused += () => Ink(RestingInk, focused: false);

        void Ink(ColorRgba ink, bool focused)
        {
            caption.Color = ink;
            prompt?.NameColor = ink;
            bar.Visible = focused;
        }
    }
}
