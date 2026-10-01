using System.Numerics;
using Capsule;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// An invisible area that shows one line of text across its top while the player stands in it.
/// Each placement sizes its own area and writes its own line.
/// </summary>
public sealed class HintZone : Entity
{
    private const string Channel = "Zones";

    private readonly Label _hint;

    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <summary>The line shown while the player is inside.</summary>
    [Authorable(Required = true)]
    public string Text { get; private set; } = string.Empty;

    /// <param name="spawn">The area's top-left corner.</param>
    public HintZone(EntitySpawn spawn)
        : base(spawn)
    {
        _hint = new Label(CapsuleAssets.Fonts.MenuFont, Text)
        {
            Pivot = Pivot.Top,
            Offset = new Vector2(Size.X / 2f, 2f),
            Visible = false,
        };
        Add(_hint);

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

    private void OnPlayerEntered(ColliderContact2D contact) => _hint.Visible = true;

    private void OnPlayerExited(ColliderContact2D contact) => _hint.Visible = false;
}
