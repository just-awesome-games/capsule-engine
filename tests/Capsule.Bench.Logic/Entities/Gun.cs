namespace Capsule.Bench.Logic.Entities;

/// <summary>A gun that fires 34 pooled bullets a step from its position in a turning fan, at <c>speed</c> pixels a step.</summary>
/// <remarks>About two thousand colliders join and leave the scene each second.</remarks>
public sealed class Gun : Entity
{
    private const int PerStep = 34;

    private readonly EntityPool<Bullet> _pool = new(() => new Bullet(), 64 * PerStep);
    private readonly float _speed;
    private int _fired;

    public Gun(Vector2 position, float speed = 5f)
        : base(position)
    {
        _speed = speed;
    }

    protected override void OnStep(in StepContext context)
    {
        for (int index = 0; index < PerStep; index++)
        {
            // A golden-angle turn per shot spreads each step's shots round the whole circle.
            float angle = (_fired++ % 1_000) * 2.3999631f;
            Bullet bullet = _pool.Take();
            bullet.Launch(Position, _speed * new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle)));
            Scene!.Add(bullet);
        }
    }
}
