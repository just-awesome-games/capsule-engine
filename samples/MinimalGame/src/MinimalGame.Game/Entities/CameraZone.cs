using System.Numerics;
using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// An invisible area that holds the camera inside it while the player stands in it, and hands back the
/// bounds held before the first zone when the player leaves every zone. The view slides in on a set
/// curve and eases back out on the camera's own chase.
/// </summary>
public sealed class CameraZone : Entity
{
    private static readonly BoundsTransition SlideIn = BoundsTransition.Eased(0.5f, Ease.InOutSine);

    private bool _holdsPlayer;
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

    // Stepping in from a neighbouring zone carries over the bounds that zone found.
    private void OnPlayerEntered(ColliderContact2D contact)
    {
        _outside = Scene.FindFirst<CameraZone>(zone => zone._holdsPlayer) is { } held ? held._outside : Scene.Camera.Bounds;
        _holdsPlayer = true;
        Scene.Camera.SetBounds(new Rect(Position, Size), SlideIn);
    }

    // Leaving into a zone the player still stands in hands the bounds to that zone. A scene tearing down
    // with the player inside ends the contact after this zone has left it.
    private void OnPlayerExited(ColliderContact2D contact)
    {
        _holdsPlayer = false;
        if (SceneOrNull is not { } scene)
        {
            return;
        }

        if (scene.FindFirst<CameraZone>(zone => zone._holdsPlayer) is { } held)
        {
            scene.Camera.SetBounds(new Rect(held.Position, held.Size), SlideIn);
        }
        else
        {
            scene.Camera.Bounds = _outside;
        }
    }
}
