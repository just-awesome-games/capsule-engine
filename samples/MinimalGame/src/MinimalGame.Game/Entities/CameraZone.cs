using System.Numerics;
using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// An invisible area that holds the camera inside it while the player stands in it, and hands back the
/// bounds it found when the player leaves. The view slides in on a set curve and eases back out on the
/// camera's own chase.
/// </summary>
public sealed class CameraZone : Entity
{
    private Rect? _outside;

    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <param name="spawn">The area's top-left corner.</param>
    public CameraZone(EntitySpawn spawn)
        : base(spawn)
    {
        BoxCollider2D area = new(Size) { ReportsContacts = true };
        area.SetFilter(CollisionLayers.Player);
        area.ContactEntered += OnPlayerEntered;
        area.ContactExited += OnPlayerExited;
        Add(area);
    }

    private void OnPlayerEntered(ColliderContact2D contact)
    {
        _outside = Scene.Camera.Bounds;
        Scene.Camera.EaseBounds(new Rect(Position, Size), 0.5f, Ease.InOutSine);
    }

    // A scene tearing down with the player inside ends the contact after this zone has left it.
    private void OnPlayerExited(ColliderContact2D contact)
    {
        if (SceneOrNull is { } scene)
        {
            scene.Camera.Bounds = _outside;
        }
    }
}
