namespace Capsule.Collision;

/// <summary>Which member of the fixed shape union a <see cref="Shape"/> is.</summary>
public enum ShapeKind
{
    /// <summary>One point and a positive radius.</summary>
    Circle,

    /// <summary>A segment and a positive radius: the region within that radius of the segment.</summary>
    Capsule,

    /// <summary>An axis-aligned rectangle with no radius; the narrowphase's fast path.</summary>
    Box,

    /// <summary>A convex polygon of three to eight points, optionally rounded by a radius.</summary>
    Polygon,
}
