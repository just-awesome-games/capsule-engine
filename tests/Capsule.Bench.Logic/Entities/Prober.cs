using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Bench.Logic.Entities;

/// <summary>
/// A step of collision queries against the room: 64 rays down and 64 ray-alls across, 64 box
/// overlaps, four map-length diagonal casts and one mover step, every result folded into
/// <see cref="Found"/> so nothing is elided.
/// </summary>
public sealed class Prober : Entity
{
    private const float RoomWidth = 256f * World.TileSize;

    private const float RoomHeight = 48f * World.TileSize;

    private static readonly Shape2D Body = Shape2D.Box(Vector2.Zero, new Vector2(12f, 24f));

    private readonly KinematicBody2D _mover;
    private readonly RayHit2D[] _hits = new RayHit2D[16];
    private readonly Contact2D[] _contacts = new Contact2D[32];
    private CollisionFilter _filter;
    private float _direction = 1f;

    public Prober(EntitySpawn spawn)
        : base(spawn)
    {
        BoxCollider2D collider = new(new Vector2(12f, 24f));
        collider.SetFilter(CollisionLayers.Solid, CollisionLayers.Platform);
        Add(collider);

        _mover = new KinematicBody2D(collider);
        _mover.BlocksOn(CollisionLayers.Solid, CollisionLayers.Platform);
        Add(_mover);
    }

    public long Found { get; private set; }

    protected override void OnStart() =>
        _filter = Scene!.Collision.CreateFilter(CollisionLayers.Solid, CollisionLayers.Platform, CollisionLayers.Actor);

    protected override void OnStep(in StepContext context)
    {
        CollisionWorld2D world = Scene!.Collision;
        long step = context.Tick;
        long found = 0;

        for (int index = 0; index < 64; index++)
        {
            Vector2 origin = new(((index * 61) + step) % RoomWidth, 34f * World.TileSize);

            if (world.Raycast(origin, Vector2.UnitY, 256f, _filter, out RayHit2D hit))
            {
                found += hit.Target.CellY;
            }

            found += world.RaycastAll(origin, Vector2.UnitX, 128f, _filter, _hits);
        }

        for (int index = 0; index < 64; index++)
        {
            Vector2 corner = new(((index * 37) + step) % RoomWidth, 37f * World.TileSize);
            found += world.OverlapBoxAll(Aabb2D.FromCorner(corner, new Vector2(24f, 24f)), _filter, _contacts);
        }

        for (int index = 0; index < 4; index++)
        {
            float offset = ((index * 13) + step) % World.TileSize;

            if (world.ShapeCast(Body, new Vector2(offset, offset), new Vector2(RoomWidth, RoomHeight), _filter, out ShapeCastHit2D hit))
            {
                found += hit.Target.CellX;
            }
        }

        MoveResult2D result = _mover.Move(new Vector2(_direction * 2f, 4f));
        if (_mover.IsOnWall)
        {
            _direction = -_direction;
        }

        Found += found + result.ContactCount;
    }
}
