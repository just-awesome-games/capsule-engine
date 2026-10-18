namespace Capsule.Bench.Logic.Cameras;

/// <summary>Frames one viewport about one point and never moves, which keeps a capture independent of camera travel.</summary>
public sealed class ParkedCamera : Camera
{
    public ParkedCamera()
        : this(World.ViewportSize / 2f, World.ViewportSize)
    {
    }

    public ParkedCamera(Vector2 centre)
        : this(centre, World.ViewportSize)
    {
    }

    public ParkedCamera(Vector2 centre, Vector2 viewport)
    {
        ViewportSize = viewport;
        Teleport(centre);
    }
}
