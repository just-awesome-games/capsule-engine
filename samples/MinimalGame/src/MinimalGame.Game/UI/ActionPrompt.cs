namespace MinimalGame.Game.UI;

/// <summary>
/// What the player presses for an action: the <see cref="GamePrompts"/> art for the button the action
/// shows, else that button's name, which follows the device, the pad, the keyboard layout, the language
/// and a rebind by itself.
/// </summary>
/// <remarks>
/// The art draws Xbox pad buttons, and a pad of another family shows its own button names instead. A
/// screen entity holding the prompt fits it as it shows. A world entity holding it places it from a late
/// step, once the prompt has stepped.
/// </remarks>
public sealed class ActionPrompt : Component
{
    /// <summary>The prompt's height, which is the art's, with its top-left corner on its holder.</summary>
    public const float Height = 16f;

    private readonly InputAction _action;

    // Any frame preloads the texture every prompt draws from because they are all cut from one sheet.
    private readonly SpriteRenderer _art = new(CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.PadSouth) { Visible = false };

    private readonly Label _name = new(CapsuleAssets.Fonts.MenuFont)
    {
        Size = new Vector2(0f, Height),
        VerticalAlignment = VerticalAlignment.Middle,
    };

    // Whether the label names the action's button, set the first time the art does not draw it.
    private bool _named;

    /// <param name="holder">The entity the prompt draws on.</param>
    /// <param name="action">The action whose button is shown.</param>
    public ActionPrompt(Entity holder, InputAction action)
    {
        _action = action;
        holder.Add(_art);
        holder.Add(_name);
        holder.Add(this);
    }

    /// <summary>The width the prompt draws, for a world holder to place what sits beside it.</summary>
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
        // The sheet draws an Xbox pad's buttons, which a generic pad prints too.
        InputButton button = context.Input.ButtonFor(_action);
        bool drawsArt = GamePrompts.TryGet(button, out Sprite art)
            && (button.Device == InputDevice.KeyboardMouse || context.Input.PadFamily is PadFamily.Xbox or PadFamily.Generic);
        if (drawsArt)
        {
            _art.Sprite = art;
            if (_named || !_art.Visible)
            {
                _art.Visible = true;
                _name.Text = string.Empty;
                _named = false;
            }
        }
        else if (!_named)
        {
            _art.Visible = false;
            _name.SetText(_action);
            _named = true;
        }
    }
}
