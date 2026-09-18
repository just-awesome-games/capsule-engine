using System.Numerics;

namespace Capsule.Bench.Logic;

/// <summary>One world unit is one pixel of the render surface, Y-down, so a capture is the surface.</summary>
public static class World
{
    public static readonly Vector2 ViewportSize = new(640f, 360f);

    public static readonly Vector2 HdViewportSize = new(1920f, 1080f);

    public const int TileSize = 16;
}
