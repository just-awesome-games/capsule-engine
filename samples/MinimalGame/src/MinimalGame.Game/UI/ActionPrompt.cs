using System.Numerics;
using Capsule;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// What the player presses for an action: the <see cref="GamePrompts"/> art for the first button the action
/// has on the device they used last, or that button's name where there is no art, or nothing where the
/// action has no button there. It follows the device and a rebind on its own, and its parent places it from
/// its late step, once the prompt has stepped.
/// </summary>
public sealed class ActionPrompt : Entity
{
    /// <summary>The prompt's height, which is the art's, with its top-left corner on the entity.</summary>
    public const float Height = 16f;

    private readonly InputAction _action;

    // Every prompt is cut from one sheet, so any frame preloads the texture they all draw from.
    private readonly SpriteRenderer _art = new(CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.PadSouth) { Visible = false };

    private readonly Label _name = new(CapsuleAssets.Fonts.MenuFont)
    {
        Size = new Vector2(0f, Height),
        VerticalAlignment = VerticalAlignment.Middle,
    };

    // The button shown, or null before the first step.
    private InputButton? _shown;

    /// <param name="parent">The entity that places the prompt.</param>
    /// <param name="action">The action whose button is shown.</param>
    public ActionPrompt(Entity parent, InputAction action)
        : base(parent)
    {
        _action = action;
        Add(_art);
        Add(_name);
    }

    /// <summary>The width the prompt draws, so its parent can place what sits beside it.</summary>
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
        foreach (InputButton bound in Run.Input.Bindings.ButtonsFor(_action))
        {
            if (bound.Device == context.Input.ActiveDevice)
            {
                button = bound;
                break;
            }
        }

        if (button == _shown)
        {
            return;
        }

        _shown = button;
        _art.Visible = GamePrompts.TryGet(button, out Sprite art);
        if (_art.Visible)
        {
            _art.Sprite = art;
        }

        _name.Text = _art.Visible || button.IsNone ? string.Empty : button.Name;
    }
}
