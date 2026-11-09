using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One convex polygon of flat colour as the simulation wants it drawn, its outline at the end of the previous
/// step and now. The renderer interpolates each point between the two and fills the polygon as a fan of
/// triangles from its first point.
/// </summary>
/// <remarks>
/// The points run in order around the outline, either way round. Where the frame's sprites snap to whole
/// pixels, the whole polygon moves by however far its first point moves to land on the grid. Its shape holds
/// as it travels. An outline that is not convex still draws its fan from the first point, and a point that is
/// not finite draws nothing. A polygon draws with the white texel through the running renderer's material, in
/// draw order among the sprites.
/// </remarks>
public readonly ref struct PolygonIntent
{
    /// <summary>The outline at the end of the previous step, in the drawn space's units.</summary>
    public ReadOnlySpan<Vector2> PreviousPoints { get; }

    /// <summary>The outline now, in the drawn space's units.</summary>
    public ReadOnlySpan<Vector2> Points { get; }

    /// <summary>The colour filled, with straight alpha.</summary>
    public ColorRgba Color { get; }

    /// <summary>How the polygon's colour combines with what is already drawn.</summary>
    public BlendMode Blend { get; }

    /// <summary>A polygon whose outline moves from <paramref name="previousPoints"/> to <paramref name="points"/> across the step.</summary>
    /// <param name="previousPoints">The outline at the end of the previous step, in the drawn space's units.</param>
    /// <param name="points">The outline now, point for point with <paramref name="previousPoints"/>.</param>
    /// <param name="color">The colour filled, with straight alpha.</param>
    /// <param name="blend">How the polygon's colour combines with what is already drawn.</param>
    public PolygonIntent(ReadOnlySpan<Vector2> previousPoints, ReadOnlySpan<Vector2> points, ColorRgba color, BlendMode blend = BlendMode.Alpha)
    {
        if (points.Length < 3)
        {
            throw new ArgumentException($"A polygon takes three points or more, not {points.Length}.", nameof(points));
        }

        if (previousPoints.Length != points.Length)
        {
            throw new ArgumentException(
                $"The previous outline holds {previousPoints.Length} points and the current one {points.Length}. Give both the same points in the same order.",
                nameof(previousPoints));
        }

        PreviousPoints = previousPoints;
        Points = points;
        Color = color;
        Blend = blend;
    }

    /// <summary>A polygon whose outline stays at <paramref name="points"/> across the step.</summary>
    /// <param name="points">The outline, in the drawn space's units.</param>
    /// <param name="color">The colour filled, with straight alpha.</param>
    /// <param name="blend">How the polygon's colour combines with what is already drawn.</param>
    public PolygonIntent(ReadOnlySpan<Vector2> points, ColorRgba color, BlendMode blend = BlendMode.Alpha)
        : this(points, points, color, blend)
    {
    }
}
