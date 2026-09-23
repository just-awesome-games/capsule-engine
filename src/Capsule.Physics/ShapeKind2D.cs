namespace Capsule.Physics;

/// <summary>Which member of the fixed shape union a <see cref="Shape2D"/> is.</summary>
public enum ShapeKind2D
{
    /// <summary>One point and a positive radius.</summary>
    Circle,

    /// <summary>A segment and a positive radius, covering the region within that radius of the segment.</summary>
    Capsule,

    /// <summary>An axis-aligned rectangle with no radius, cheaper to test than a polygon.</summary>
    Box,

    /// <summary>A convex polygon of three or four points, optionally rounded by a radius.</summary>
    Polygon,

    /// <summary>
    /// A segment with no interior, used by one side of a grid collider. No public factory builds one.
    /// </summary>
    Segment,
}
