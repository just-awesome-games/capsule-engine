using MinimalGame.Game.UI;

namespace MinimalGame.Game.Entities;

/// <summary>
/// An invisible area that types a hint across its top while the player stands in it, after an optional
/// prompt for the action the hint is about.
/// </summary>
/// <remarks>
/// Each placement sizes its own area and writes its own hint. A hint wider than the area wraps, and its
/// lines take turns one at a time. The zone reacts itself because the hint is its own.
/// </remarks>
public sealed class HintZone : Entity
{
    private const string Channel = "Zones";

    // World units between the prompt and the line, and above the pair.
    private const float Gap = 4f;
    private const float Top = 2f;

    // Steps each character takes to type, and steps a typed line holds before the next.
    private const int CharacterTicks = 3;
    private const int HoldTicks = 90;

    private readonly Label _hint;
    private readonly ActionPrompt? _prompt;
    private readonly Sequence _typing = new();

    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <summary>The hint typed while the player is inside, read as <see cref="Label.RichText"/>.</summary>
    [Authorable(Required = true)]
    public TextKey Text { get; private set; }

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
            Wrap = TextWrap.Word,
            MaxLines = 1,
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

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context) => _typing.Step();

    protected override void OnDebugDraw() => DebugDraw.Rect(Channel, new Rect(Position, Size));

    protected override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Hint", _hint.Text);
        panel.Field("Showing", _hint.Visible);
    }

    // The prompt and the shown line are centred together across the top, and the hint wraps in the width the
    // prompt leaves. The prompt has stepped by now, and its width is this step's.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        float lead = _prompt is { Width: > 0f } prompt ? prompt.Width + Gap : 0f;
        _hint.Size = new Vector2(Size.X - lead, ActionPrompt.Height);
        float left = (Size.X - lead - _hint.ContentSize.X) / 2f;

        _prompt?.Position = new Vector2(left, Top);
        _hint.Offset = new Vector2(left + lead, Top);
    }

    private void OnPlayerEntered(ColliderContact2D contact)
    {
        Show(true);
        _typing.Start(Type());
    }

    private void OnPlayerExited(ColliderContact2D contact)
    {
        _typing.Stop();
        Show(false);
    }

    private void Show(bool shown)
    {
        _hint.Visible = shown;
        _hint.FirstLine = 0;
        _hint.VisibleCharacters = 0;
        _prompt?.Visible = shown;
    }

    // Types the shown line, holds it, and turns to the next, back to the first after the last. A hint of one
    // line types once and stays.
    private IEnumerable<Wait> Type()
    {
        while (true)
        {
            int end = _hint.LineStart(_hint.FirstLine + 1);
            for (int shown = _hint.LineStart(_hint.FirstLine) + 1; shown <= end; shown++)
            {
                _hint.VisibleCharacters = shown;
                yield return Wait.Ticks(CharacterTicks);
            }

            if (_hint.LineCount <= 1)
            {
                yield break;
            }

            yield return Wait.Ticks(HoldTicks);
            _hint.FirstLine = (_hint.FirstLine + 1) % _hint.LineCount;
        }
    }
}
