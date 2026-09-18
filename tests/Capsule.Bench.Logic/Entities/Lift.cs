using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Bench.Logic.Entities;

// A collider on a layer the crowd blocks on: without one, no walker's filter reaches anything in
// the broadphase tree and the tree is never asked a real question.
public sealed class Lift : Entity
{
    public Lift(EntitySpawn spawn)
        : base(spawn)
    {
        BoxCollider2D collider = new(new Vector2(32f, 8f));
        collider.Layer = CollisionLayers.Platform;
        Add(collider);
    }
}
