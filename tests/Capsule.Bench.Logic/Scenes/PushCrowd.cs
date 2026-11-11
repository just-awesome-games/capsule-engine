using Capsule.Bench.Logic.Cameras;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 1 000 floating bodies closing on one body their layer moves, from a ring of staggered distances: every
/// crowd member's move looks for a body to shove among a crowd it cannot move.
/// </summary>
[Workload(WorkloadKind.Simulation)]
public sealed class PushCrowd : Scene
{
    private const string CrowdLayer = "crowd";

    public PushCrowd()
    {
        Camera = new ParkedCamera(Vector2.Zero);
        Pushed pushed = new();
        Add(pushed);

        int crowd = Workloads.Scaled(1_000);
        for (int index = 0; index < crowd; index++)
        {
            Add(new Pusher(pushed, index, crowd));
        }
    }

    private sealed class Pushed : Entity
    {
        internal Pushed()
            : base(Vector2.Zero)
        {
            BoxCollider2D collider = new(new Vector2(12f, 24f)) { Layer = CollisionLayers.Actor };
            Add(collider);
            Add(new KinematicBody2D(collider) { MovedBy = new(CrowdLayer) });
        }
    }

    private sealed class Pusher : Entity
    {
        private readonly Entity _target;
        private readonly KinematicBody2D _body;

        internal Pusher(Entity target, int index, int count)
            : base(Start(index, count))
        {
            _target = target;
            BoxCollider2D collider = new(new Vector2(12f, 12f)) { Layer = CrowdLayer };
            Add(collider);
            _body = new KinematicBody2D(collider);
            Add(_body);
        }

        private static Vector2 Start(int index, int count)
        {
            // A golden-angle turn per body spreads the ring, and the distance staggers the arrivals.
            float angle = index * 2.3999631f;
            float radius = 40f + (360f * index / count);
            return radius * new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle));
        }

        protected override void OnStep(in StepContext context)
        {
            Vector2 toTarget = _target.Position - Position;
            float distance = toTarget.Length();
            if (distance > 1f)
            {
                _body.Move(toTarget / distance);
            }
        }
    }
}
