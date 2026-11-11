using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 2 000 seekers closing on a circling hero that fires 50 pooled bullets a step, 3 000 in flight: a bullet
/// leaves on the first seeker it touches and sends that seeker back to the rim.
/// </summary>
/// <remarks>
/// Row <c>HordePiled</c> makes the seekers immune and the bullets pierce: every seeker ends up on the
/// hero and every bullet crosses the pile. Both rows allocate as each pooled bullet's contact buffers grow
/// to the most seekers it has touched at once.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = Piled)]
public sealed class Horde : Scene
{
    private const string Piled = "HordePiled";
    private const string SeekerLayer = "seeker";
    private const float Rim = 700f;

    public Horde()
    {
        Camera = new ParkedCamera(Vector2.Zero);
        bool piled = Workloads.Running == Piled;
        Hero hero = new(Workloads.Scaled(50), piled);
        Add(hero);

        int seekers = Workloads.Scaled(2_000);
        for (int index = 0; index < seekers; index++)
        {
            Add(new Seeker(hero, index, seekers));
        }
    }

    private static Vector2 OnRim(int index, float radius)
    {
        float angle = index * 2.3999631f;
        return radius * new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle));
    }

    private sealed class Hero : Entity
    {
        private readonly EntityPool<Shot> _pool;
        private readonly int _perStep;
        private int _fired;

        internal Hero(int perStep, bool piercing)
            : base(Vector2.Zero)
        {
            _perStep = perStep;
            _pool = new EntityPool<Shot>(() => new Shot(piercing), 64 * perStep);

            BoxCollider2D hurt = new(new Vector2(12f, 24f)) { Layer = CollisionLayers.Sensor, ReportsContacts = true };
            hurt.Detects = new(SeekerLayer);
            Add(hurt);
            Add(new SpriteRenderer(CapsuleAssets.Sprites.WalkerSheet.Frames.Walk0));
        }

        protected override void OnStep(in StepContext context)
        {
            float turn = context.Tick * 0.01f;
            Position = 150f * new Vector2(DeterministicMath.Cos(turn), DeterministicMath.Sin(turn));

            for (int index = 0; index < _perStep; index++)
            {
                Shot shot = _pool.Take();
                shot.Launch(Position, OnRim(_fired++ % 1_000, 6f));
                Scene!.Add(shot);
            }
        }
    }

    private sealed class Seeker : Entity
    {
        private readonly Hero _hero;
        private readonly SpriteAnimator _animator;
        private int _spawns;

        internal Seeker(Hero hero, int index, int count)
            : base(OnRim(index, Rim * (index + 1) / count))
        {
            _hero = hero;
            _spawns = index;

            Add(new BoxCollider2D(new Vector2(12f, 12f)) { Layer = SeekerLayer });
            SpriteRenderer renderer = new(CapsuleAssets.Sprites.WalkerSheet.Frames.Walk0);
            Add(renderer);
            _animator = new SpriteAnimator(renderer);
            Add(_animator);
        }

        internal void Respawn() => Position = _hero.Position + OnRim(_spawns += 7, Rim);

        protected override void OnStart() => _animator.Play(CapsuleAssets.Sprites.WalkerSheet.Clips.Walk);

        protected override void OnStep(in StepContext context)
        {
            Vector2 toHero = _hero.Position - Position;
            float distance = toHero.Length();
            if (distance > 0.6f)
            {
                Position += toHero * (0.6f / distance);
            }
        }
    }

    private sealed class Shot : Entity
    {
        private const int LifeSteps = 60;

        private Vector2 _velocity;
        private int _life;

        internal Shot(bool piercing)
            : base(Vector2.Zero)
        {
            BoxCollider2D collider = new(new Vector2(4f, 4f)) { ReportsContacts = true };
            collider.Detects = new(SeekerLayer);
            if (!piercing)
            {
                collider.ContactEntered += contact =>
                {
                    if (Scene is not null && contact.OtherCollider?.Entity is Seeker seeker)
                    {
                        seeker.Respawn();
                        Scene.Remove(this);
                    }
                };
            }

            Add(collider);
            Add(new SpriteRenderer(SpriteField.Tile));
        }

        internal void Launch(Vector2 origin, Vector2 velocity)
        {
            Position = origin;
            _velocity = velocity;
            _life = LifeSteps;
        }

        protected override void OnStep(in StepContext context)
        {
            Position += _velocity;

            if (--_life <= 0)
            {
                Scene?.Remove(this);
            }
        }
    }
}
