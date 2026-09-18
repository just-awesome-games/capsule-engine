using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Bench.Logic.Entities;

public sealed class Post : Entity
{
    public Post(EntitySpawn spawn)
        : base(spawn)
    {
        BoxCollider2D collider = new(new Vector2(12f, 12f));
        collider.Layer = CollisionLayers.Actor;
        Add(collider);
    }
}
