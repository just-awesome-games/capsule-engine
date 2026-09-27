using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// A low block that travels its placement's path point to point, easing each leg. A body moved by its
/// layer rides it and is shoved by it.
/// </summary>
public sealed class Shuttle : Entity
{
    private const int LegTicks = 60;
    private static readonly Vector2 Size = new(24f, 8f);

    private readonly Vector2 _origin;
    private readonly bool _loops;
    private Tween _leg;
    private int _from;
    private int _step = 1;

    /// <summary>
    /// The points the block travels, relative to its placement. A path that ends where it starts loops,
    /// and any other runs to its end and back.
    /// </summary>
    [Authorable(Required = true)]
    public Vector2[] Path { get; private set; } = [];

    public Shuttle(EntitySpawn spawn)
        : base(spawn)
    {
        if (Path.Length < 2)
        {
            throw new InvalidOperationException(
                $"A shuttle's path has {Path.Length} points, but it travels between at least two. Add at least two points to the placement's \"path\".");
        }

        _origin = Position;
        _loops = Path[^1] == Path[0];
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Platform });
        Add(new ColorRect(Size) { Color = ColorRgba.FromHex("#5a8870") });
        _leg.Start(LegTicks, Ease.InOutSine);
    }

    protected override void OnStep(in StepContext context)
    {
        _leg.Step();
        Position = _origin + Vector2.Lerp(Path[_from], Path[_from + _step], _leg.Value);
        if (_leg.JustFinished)
        {
            _from += _step;
            if (_loops && _from == Path.Length - 1)
            {
                _from = 0;
            }
            else if (_from + _step < 0 || _from + _step >= Path.Length)
            {
                _step = -_step;
            }

            _leg.Start(LegTicks, Ease.InOutSine);
        }
    }
}
