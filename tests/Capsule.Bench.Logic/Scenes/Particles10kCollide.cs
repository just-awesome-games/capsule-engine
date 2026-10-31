using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 10 000 particles falling and bouncing through the walled room of hills in <c>particles-10k-collide.scene.json</c>,
/// each casting one ray a step against the room's grid.
/// </summary>
/// <remarks>
/// Row <c>Rain2k</c> drops 2 000 particles a second from the ceiling, each dying on the terrain it meets into a
/// three-particle splash from a second emitter.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = Rain)]
public sealed class Particles10kCollide : Scene
{
    private const string Rain = "Rain2k";

    public Particles10kCollide(SceneContent content)
        : base(content)
    {
        TileMap room = FindSingle<TileMap>();
        Camera = new ParkedCamera(room.Size / 2f);
        CollisionMask solid = new("solid");

        // The room's open space, inside its one-tile walls and above its hills and floor.
        Vector2 open = room.Size - new Vector2(2f * World.TileSize, 4f * World.TileSize);
        Vector2 centre = new(room.Size.X / 2f, World.TileSize + (open.Y / 2f));

        if (Workloads.Running == Rain)
        {
            ParticleEmitter splash = new(SpriteField.Tile, capacity: 6_000)
            {
                Direction = -Vector2.UnitY,
                Spread = 120f,
                Speed = new FloatRange(20f, 60f),
                Gravity = new Vector2(0f, 300f),
                Lifetime = new FloatRange(0.3f, 0.5f),
            };
            ParticleEmitter drops = new(SpriteField.Tile, capacity: 2_000)
            {
                Offset = new Vector2(centre.X, World.TileSize),
                Shape = EmitShape.Rect(open.X, 0f),
                Direction = Vector2.UnitY,
                Speed = new FloatRange(240f, 300f),
                Lifetime = new FloatRange(2f, 2f),
                Rate = 2_000f,
                Collision = ParticleCollision.Kill(solid),
                EmitOnDeath = new SubEmission(splash, count: 3),
                PrewarmSeconds = 1f,
            };
            Add(new Holder(drops));
            Add(new Holder(splash));

            return;
        }

        Add(new Holder(new ParticleEmitter(SpriteField.Tile, capacity: 10_000)
        {
            Offset = centre,
            Shape = EmitShape.Rect(open.X, open.Y),
            Rate = 10_000f,
            Lifetime = new FloatRange(1f, 1f),
            Speed = new FloatRange(20f, 80f),
            Spread = 360f,
            Gravity = new Vector2(0f, 300f),
            Collision = ParticleCollision.Bounce(solid, bounce: 0.5f, friction: 0.2f),
            PrewarmSeconds = 1f,
        }));
    }
}
