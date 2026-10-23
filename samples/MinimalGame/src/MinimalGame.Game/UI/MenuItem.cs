namespace MinimalGame.Game.UI;

/// <summary>
/// One menu item as wide as its slot, holding a focus box, a caption, a highlight bar and an optional
/// <see cref="ActionPrompt"/>, which says it was pressed and never what pressing means.
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

    // Handed to the menu's navigator, which is the only thing that may move this item's focus.
    internal Focusable Focusable { get; }

    /// <param name="text">The caption drawn inside the box.</param>
    /// <param name="prompt">The action whose button is shown after the caption, or null for none.</param>
    public MenuItem(TextKey text, InputAction? prompt = null)
        : this(prompt) => _caption.SetText(text);

    /// <summary>An item whose caption is set once it starts, as a row showing a setting's value is.</summary>
    /// <param name="prompt">The action whose button is shown after the caption, or null for none.</param>
    public MenuItem(InputAction? prompt = null)
        : base(Anchor.TopWide, Vector2.Zero)
    {
        // Everything is attached here, where the font and the prompt art join the scene's preload.
        // Spanning the slot makes a column's items all as wide as the widest.
        _bar = new ColorRect { Visible = false };

        _caption = new Label(CapsuleAssets.Fonts.MenuFont)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Middle,
            Color = RestingInk,
        };

        Focusable = new Focusable();

        // The caption draws over the bar attached before it.
        Add(_bar);
        Add(_caption);
        Add(Focusable);

        if (prompt is { } action)
        {
            _prompt = new ActionPrompt(this, action);
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

    /// <summary>
    /// The width in canvas pixels the caption and the prompt take as they show now, in the run's language,
    /// and never less than <see cref="MinWidth"/>.
    /// </summary>
    public float ContentWidth => Math.Max(MinWidth, _caption.ContentSize.X + Trail + (2f * HorizontalPadding));

    // The gap and the prompt after the caption, or nothing while the prompt shows nothing.
    private float Trail => _prompt is { Visible: true, Width: > 0f } shown ? PromptGap + shown.Width : 0f;

    /// <summary>The caption as it shows now, in the run's language.</summary>
    public string Caption => _caption.Text;

    /// <summary>Shows <paramref name="text"/> inside the box.</summary>
    public void SetCaption(TextKey text) => _caption.SetText(text);

    /// <summary>Shows <paramref name="text"/> inside the box with its placeholder filled by <paramref name="value"/>.</summary>
    public void SetCaption<T>(TextKey text, T value) => _caption.SetText(text, value);

    /// <summary>Whether the prompt after the caption shows. An item built with no prompt shows none.</summary>
    public bool ShowsPrompt
    {
        get => _prompt is { Visible: true };
        set => _prompt?.Visible = value;
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

    // The caption resolves against the run's language from here. Sizing here sizes the first frame.
    /// <inheritdoc/>
    protected override void OnStart() => Size = new Vector2(ContentWidth, BoxHeight);

    // The caption and its prompt are centred together, and the box grows to hold both. Focused ink never
    // runs off the bar. The prompt has stepped by now, and its width is this step's.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        float caption = _caption.ContentSize.X;
        float trail = Trail;
        float width = ContentWidth;
        if (Size.X != width)
        {
            Size = new Vector2(width, BoxHeight);
        }

        if (_prompt is null)
        {
            return;
        }

        // The caption fills the item. Its bounds are the slot the item was given, which may be wider.
        float slot = _caption.Bounds.Size.X;
        _caption.Offset = new Vector2(-trail / 2f, 0f);
        _prompt.Position = new Vector2(((slot + caption - trail) / 2f) + PromptGap, (BoxHeight - ActionPrompt.Height) / 2f);
    }
}
