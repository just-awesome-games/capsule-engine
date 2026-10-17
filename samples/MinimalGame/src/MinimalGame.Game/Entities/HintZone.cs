using System.Numerics;
using Capsule;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Entities;

/// <summary>
/// An invisible area that shows one line of text across its top while the player stands in it,
/// after an optional prompt for the action the line is about. Each placement sizes its own area and writes
/// its own line. The line is its own, so it reacts itself.
/// </summary>
public sealed class HintZone : Entity
{
    private const string Channel = "Zones";

    // World units between the prompt and the line, and above the pair.
    private const float Gap = 4f;
    private const float Top = 2f;

    private readonly Label _hint;
    private readonly ActionPrompt? _prompt;

    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <summary>The line shown while the player is inside, read as <see cref="Label.RichText"/>.</summary>
    [Authorable(Required = true)]
    public string Text { get; private set; } = string.Empty;

    /// <summary>The input action whose button is shown before the line, or empty for none.</summary>
    [Authorable]
    public string Prompt { get; private set; } = string.Empty;

    /// <param name="spawn">The area's top-left corner.</param>
    public HintZone(EntitySpawn spawn)
        : base(spawn)
    {
        _hint = new Label(CapsuleAssets.Fonts.MenuFont, Text)
        {
            Size = new Vector2(0f, ActionPrompt.Height),
            VerticalAlignment = VerticalAlignment.Middle,
            RichText = true,
        };
        Add(_hint);

        if (Prompt.Length > 0)
        {
            _prompt = new ActionPrompt(this, new InputAction(Prompt));
        }

        Show(false);

        BoxCollider2D area = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        area.ContactEntered += OnPlayerEntered;
        area.ContactExited += OnPlayerExited;
        Add(area);
    }

    protected override void OnDebugDraw() => DebugDraw.Rect(Channel, new Rect(Position, Size));

    protected override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Hint", _hint.Text);
        panel.Field("Showing", _hint.Visible);
    }

    // The prompt and the line are centred together across the top. The prompt has stepped by now, so its
    // width is this step's.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        float lead = _prompt is { Width: > 0f } prompt ? prompt.Width + Gap : 0f;
        float left = (Size.X - lead - _hint.ContentSize.X) / 2f;

        _prompt?.Position = new Vector2(left, Top);
        _hint.Offset = new Vector2(left + lead, Top);
    }

    private void OnPlayerEntered(ColliderContact2D contact) => Show(true);

    private void OnPlayerExited(ColliderContact2D contact) => Show(false);

    private void Show(bool shown)
    {
        _hint.Visible = shown;
        _prompt?.Visible = shown;
    }
}
