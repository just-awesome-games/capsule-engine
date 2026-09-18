using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Cameras;

/// <summary>Sweeps a closed path of two triangle waves with different periods, so the visible region changes every step and never settles.</summary>
public sealed class LoopCamera : Camera
{
    private const int PeriodX = 600;

    private const int PeriodY = 420;

    private readonly Vector2 _origin;
    private readonly Vector2 _reach;

    public LoopCamera(Vector2 origin, Vector2 reach)
    {
        _origin = origin;
        _reach = reach;
        ViewportSize = World.ViewportSize;
        Teleport(origin);
    }

    protected override void OnLateStep(in StepContext context) =>
        Center = _origin + new Vector2(_reach.X * Triangle(context.Tick, PeriodX), _reach.Y * Triangle(context.Tick, PeriodY));

    private static float Triangle(long tick, int period)
    {
        long phase = tick % period;
        long half = period / 2;

        return (phase < half ? phase : period - phase) / (float)half;
    }
}
