namespace Capsule.Bench.Logic.Cameras;

/// <summary>Holds one viewport about a start point for a number of steps, then moves straight down at a fixed speed to a floor and holds there.</summary>
public sealed class DescentCamera : Camera
{
    private readonly Vector2 _start;
    private readonly float _floor;
    private readonly int _holdSteps;
    private readonly float _speed;

    public DescentCamera(Vector2 start, float floor, int holdSteps, float speed, Vector2 viewport)
    {
        _start = start;
        _floor = floor;
        _holdSteps = holdSteps;
        _speed = speed;
        ViewportSize = viewport;
        Teleport(start);
    }

    protected override void OnLateStep(in StepContext context)
    {
        long moving = Math.Max(0, context.Tick - _holdSteps);
        Center = new Vector2(_start.X, Math.Min(_floor, _start.Y + (moving * _speed)));
    }
}
