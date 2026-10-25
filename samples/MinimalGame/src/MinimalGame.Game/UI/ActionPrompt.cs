namespace MinimalGame.Game.UI;

/// <summary>
/// What the player presses for an action: the <see cref="GamePrompts"/> art for the first button the action
/// has on the device they used last, else the character a key prints, else the <see cref="ButtonNames"/>
/// name in the run's language, or nothing where the action has no button there. It follows the device, the
/// pad, the keyboard layout, the language and a rebind on its own.
/// </summary>
/// <remarks>
/// The art draws Xbox pad buttons, and a pad of another family shows its own button names instead. The
/// parent places the prompt from its late step, once the prompt has stepped.
/// </remarks>
public sealed class ActionPrompt : Entity
{
    /// <summary>The prompt's height, which is the art's, with its top-left corner on the entity.</summary>
    public const float Height = 16f;

    private readonly InputAction _action;

    // Any frame preloads the texture every prompt draws from because they are all cut from one sheet.
    private readonly SpriteRenderer _art = new(CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.PadSouth) { Visible = false };

    private readonly Label _name = new(CapsuleAssets.Fonts.MenuFont)
    {
        Size = new Vector2(0f, Height),
        VerticalAlignment = VerticalAlignment.Middle,
    };

    // The button shown, the pad family and key character it was shown for, or null before the first step.
    private InputButton? _shown;
    private PadFamily _shownFamily;
    private string? _shownCharacter;

    /// <param name="parent">The entity that places the prompt.</param>
    /// <param name="action">The action whose button is shown.</param>
    public ActionPrompt(Entity parent, InputAction action)
        : base(parent)
    {
        _action = action;
        Add(_art);
        Add(_name);
    }

    /// <summary>The width the prompt draws, for its parent to place what sits beside it.</summary>
    public float Width => _art.Visible ? _art.Sprite.Region.Width : _name.ContentSize.X;

    /// <summary>The colour a button's name draws in. Art keeps its own colours.</summary>
    public ColorRgba NameColor
    {
        get => _name.Color;
        set => _name.Color = value;
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        InputButton button = InputButton.None;
        foreach (InputButton bound in Run.Input.ButtonsFor(_action))
        {
            if (bound.Device == context.Input.ActiveDevice)
            {
                button = bound;
                break;
            }
        }

        // The sheet draws an Xbox pad's buttons, which a generic pad prints too. A key's character is the
        // same string until the layout changes, and a reference check sees the change. A named label
        // follows the language itself.
        PadFamily family = context.Input.PadFamily;
        bool drawsArt = GamePrompts.TryGet(button, out Sprite art)
            && (button.Device == InputDevice.KeyboardMouse || family is PadFamily.Xbox or PadFamily.Generic);
        string? character = drawsArt ? null : context.Input.KeyCharacter(button);
        if (button == _shown && family == _shownFamily && ReferenceEquals(character, _shownCharacter))
        {
            return;
        }

        _shown = button;
        _shownFamily = family;
        _shownCharacter = character;
        _art.Visible = drawsArt;
        if (drawsArt)
        {
            _art.Sprite = art;
            _name.Text = string.Empty;
        }
        else if (character is not null)
        {
            _name.Text = character;
        }
        else if (ButtonNames.TryGet(button, family, out TextKey name))
        {
            _name.SetText(name);
        }
        else
        {
            _name.Text = string.Empty;
        }
    }
}
