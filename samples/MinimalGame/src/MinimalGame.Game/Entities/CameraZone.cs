using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// An invisible area that holds the camera inside it while the player's probe stands in it. The zone
/// reports nothing itself: the <see cref="Cameras.GameCamera"/> watches the probe and moves the bounds.
/// </summary>
public sealed class CameraZone : Entity
{
    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <param name="spawn">The area's top-left corner.</param>
    public CameraZone(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Trigger });
    }
}
