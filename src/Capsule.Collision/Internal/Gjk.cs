using System.Numerics;

namespace Capsule.Collision.Internal;

/// <summary>
/// The general narrowphase: GJK distance between two shapes' point hulls, and the conservative
/// advancement built on it that turns a translation into a time of impact. Every pair of shapes
/// the module ships is answered here; the closed-form routines elsewhere are shortcuts through the
/// same answers.
/// </summary>
internal static class Gjk
{
    private const int MaxIterations = 20;
    private const float Epsilon = 1e-9f;

    /// <summary>
    /// How far apart two shapes are, negative when they overlap by less than their radii, with a
    /// closest point on <paramref name="b"/>'s surface. The normal points from
    /// <paramref name="b"/> towards <paramref name="a"/>, and is zero only when the hulls
    /// themselves intersect, where the distance carries no direction.
    /// </summary>
    internal static float Separation(in Shape2D a, in Shape2D b, out Vector2 normal, out Vector2 point)
    {
        float hull = Distance(a, b, out Vector2 pointA, out Vector2 pointB);

        if (hull > Epsilon)
        {
            normal = Vector2.Normalize(pointA - pointB);
            point = pointB + (normal * b.Radius);
            return hull - a.Radius - b.Radius;
        }

        normal = Vector2.Zero;
        point = pointB;

        return -a.Radius - b.Radius;
    }

    /// <summary>
    /// The distance between the two hulls, with the closest point on each. Both shapes' points are
    /// already in world space; radii are the caller's to subtract.
    /// </summary>
    internal static float Distance(in Shape2D a, in Shape2D b, out Vector2 pointA, out Vector2 pointB)
    {
        Simplex simplex = default;
        simplex.Count = 1;
        simplex.V0 = Vertex(a, b, 0, 0);

        Span<int> savedA = stackalloc int[3];
        Span<int> savedB = stackalloc int[3];

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            int saved = simplex.Count;
            simplex.Save(savedA, savedB);

            switch (simplex.Count)
            {
                case 2:
                    simplex.Solve2();
                    break;
                case 3:
                    simplex.Solve3();
                    break;
                default:
                    break;
            }

            if (simplex.Count == 3)
            {
                break;
            }

            Vector2 direction = simplex.SearchDirection();
            if (Vector2.Dot(direction, direction) < Epsilon)
            {
                break;
            }

            int indexA = SupportIndex(a, -direction);
            int indexB = SupportIndex(b, direction);

            if (IsDuplicate(savedA, savedB, saved, indexA, indexB))
            {
                break;
            }

            simplex.Add(Vertex(a, b, indexA, indexB));
        }

        simplex.Witness(out pointA, out pointB);

        return Vector2.Distance(pointA, pointB);
    }

    /// <summary>
    /// The fraction of <paramref name="translation"/> at which <paramref name="moving"/> first
    /// touches <paramref name="target"/>. Both shapes' points are already in world space. Returns
    /// false when the sweep never touches, and when the pair already overlaps — a sweep out of an
    /// overlap has no time of impact to report.
    /// </summary>
    internal static bool ShapeCast(
        in Shape2D target,
        in Shape2D moving,
        Vector2 translation,
        out float fraction,
        out Vector2 point,
        out Vector2 normal)
    {
        fraction = 0f;
        point = Vector2.Zero;
        normal = Vector2.Zero;

        float radius = target.Radius + moving.Radius;

        // The advance stops here rather than at zero: a hull pair meeting exactly has no separating
        // direction to read a normal from.
        float sigma = MathF.Max(CollisionWorld2D.LinearSlop, radius - CollisionWorld2D.LinearSlop);
        float tolerance = 0.5f * CollisionWorld2D.LinearSlop;

        Simplex simplex = default;
        simplex.Count = 0;

        Vector2 witnessTarget = target.Support(-translation);
        Vector2 witnessMoving = moving.Support(translation);
        Vector2 v = witnessTarget - witnessMoving;
        Vector2 hitNormal = Vector2.Zero;
        float lambda = 0f;
        int iterations = 0;

        while (iterations < MaxIterations && v.Length() - sigma > tolerance)
        {
            iterations++;

            witnessTarget = target.Support(-v);
            witnessMoving = moving.Support(v);
            Vector2 support = witnessTarget - witnessMoving;

            v = Vector2.Normalize(v);

            float supportAlongV = Vector2.Dot(v, support);
            float travelAlongV = Vector2.Dot(v, translation);
            if (supportAlongV - sigma > lambda * travelAlongV)
            {
                if (travelAlongV <= 0f)
                {
                    return false;
                }

                lambda = (supportAlongV - sigma) / travelAlongV;
                if (lambda > 1f)
                {
                    return false;
                }

                hitNormal = -v;
                simplex.Count = 0;
            }

            // Built over moving-minus-target shifted by the advance so far; the support point above
            // stays unshifted so the plane it defines does not move.
            simplex.Add(new SimplexVertex
            {
                PointA = witnessMoving + (lambda * translation),
                PointB = witnessTarget,
                W = witnessTarget - witnessMoving - (lambda * translation),
                Weight = 1f,
                IndexA = 0,
                IndexB = 0,
            });

            switch (simplex.Count)
            {
                case 2:
                    simplex.Solve2();
                    break;
                case 3:
                    simplex.Solve3();
                    break;
                default:
                    break;
            }

            if (simplex.Count == 3)
            {
                return false;
            }

            v = simplex.ClosestPoint();
        }

        if (iterations == 0)
        {
            return false;
        }

        // The moving shape is in the first slot and the target in the second, so the surface point
        // is the target's witness pushed out by the target's radius.
        simplex.Witness(out _, out Vector2 targetWitness);

        if (Vector2.Dot(v, v) > 0f)
        {
            hitNormal = Vector2.Normalize(-v);
        }

        fraction = lambda;
        normal = hitNormal;
        point = targetWitness + (hitNormal * target.Radius);

        return true;
    }

    private static SimplexVertex Vertex(in Shape2D a, in Shape2D b, int indexA, int indexB)
    {
        Vector2 pointA = a.PointAt(indexA);
        Vector2 pointB = b.PointAt(indexB);

        return new SimplexVertex
        {
            PointA = pointA,
            PointB = pointB,
            W = pointB - pointA,
            Weight = 1f,
            IndexA = indexA,
            IndexB = indexB,
        };
    }

    private static int SupportIndex(in Shape2D shape, Vector2 direction)
    {
        int best = 0;
        float bestDot = Vector2.Dot(shape.PointAt(0), direction);

        for (int index = 1; index < shape.PointCount; index++)
        {
            float dot = Vector2.Dot(shape.PointAt(index), direction);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = index;
            }
        }

        return best;
    }

    // Termination: a support point the simplex already holds means the search cannot improve.
    // Without it an exactly touching pair loops to the iteration cap.
    private static bool IsDuplicate(ReadOnlySpan<int> savedA, ReadOnlySpan<int> savedB, int count, int indexA, int indexB)
    {
        for (int index = 0; index < count; index++)
        {
            if (savedA[index] == indexA && savedB[index] == indexB)
            {
                return true;
            }
        }

        return false;
    }

    private struct SimplexVertex
    {
        internal Vector2 PointA;
        internal Vector2 PointB;
        internal Vector2 W;
        internal float Weight;
        internal int IndexA;
        internal int IndexB;
    }

    private struct Simplex
    {
        internal SimplexVertex V0;
        internal SimplexVertex V1;
        internal SimplexVertex V2;
        internal int Count;

        internal void Add(in SimplexVertex vertex)
        {
            switch (Count)
            {
                case 0:
                    V0 = vertex;
                    break;
                case 1:
                    V1 = vertex;
                    break;
                default:
                    V2 = vertex;
                    break;
            }

            Count++;
        }

        internal readonly void Save(Span<int> savedA, Span<int> savedB)
        {
            savedA[0] = V0.IndexA;
            savedB[0] = V0.IndexB;
            savedA[1] = V1.IndexA;
            savedB[1] = V1.IndexB;
            savedA[2] = V2.IndexA;
            savedB[2] = V2.IndexB;
        }

        internal readonly Vector2 ClosestPoint() => Count switch
        {
            1 => V0.W,
            2 => (V0.Weight * V0.W) + (V1.Weight * V1.W),
            _ => Vector2.Zero,
        };

        internal readonly Vector2 SearchDirection()
        {
            if (Count == 1)
            {
                return -V0.W;
            }

            Vector2 edge = V1.W - V0.W;
            float sign = Segments.Cross(edge, -V0.W);

            return sign > 0f
                ? new Vector2(-edge.Y, edge.X)
                : new Vector2(edge.Y, -edge.X);
        }

        internal readonly void Witness(out Vector2 pointA, out Vector2 pointB)
        {
            switch (Count)
            {
                case 1:
                    pointA = V0.PointA;
                    pointB = V0.PointB;
                    break;
                case 2:
                    pointA = (V0.Weight * V0.PointA) + (V1.Weight * V1.PointA);
                    pointB = (V0.Weight * V0.PointB) + (V1.Weight * V1.PointB);
                    break;
                default:
                    pointA = (V0.Weight * V0.PointA) + (V1.Weight * V1.PointA) + (V2.Weight * V2.PointA);
                    pointB = pointA;
                    break;
            }
        }

        internal void Solve2()
        {
            Vector2 w0 = V0.W;
            Vector2 w1 = V1.W;
            Vector2 edge = w1 - w0;

            float d2 = -Vector2.Dot(w0, edge);
            if (d2 <= 0f)
            {
                V0.Weight = 1f;
                Count = 1;
                return;
            }

            float d1 = Vector2.Dot(w1, edge);
            if (d1 <= 0f)
            {
                V1.Weight = 1f;
                Count = 1;
                V0 = V1;
                return;
            }

            float inverse = 1f / (d1 + d2);
            V0.Weight = d1 * inverse;
            V1.Weight = d2 * inverse;
            Count = 2;
        }

        internal void Solve3()
        {
            Vector2 w0 = V0.W;
            Vector2 w1 = V1.W;
            Vector2 w2 = V2.W;

            // Barycentric weight numerators: edgeXY_wZ is vertex Z's share of the origin's
            // projection onto edge XY, and faceWZ is its share of the whole triangle.
            Vector2 e01 = w1 - w0;
            float edge01W0 = Vector2.Dot(w1, e01);
            float edge01W1 = -Vector2.Dot(w0, e01);

            Vector2 e02 = w2 - w0;
            float edge02W0 = Vector2.Dot(w2, e02);
            float edge02W2 = -Vector2.Dot(w0, e02);

            Vector2 e12 = w2 - w1;
            float edge12W1 = Vector2.Dot(w2, e12);
            float edge12W2 = -Vector2.Dot(w1, e12);

            float area = Segments.Cross(e01, e02);
            float faceW0 = area * Segments.Cross(w1, w2);
            float faceW1 = area * Segments.Cross(w2, w0);
            float faceW2 = area * Segments.Cross(w0, w1);

            if (edge01W1 <= 0f && edge02W2 <= 0f)
            {
                V0.Weight = 1f;
                Count = 1;
                return;
            }

            if (edge01W0 > 0f && edge01W1 > 0f && faceW2 <= 0f)
            {
                float inverse = 1f / (edge01W0 + edge01W1);
                V0.Weight = edge01W0 * inverse;
                V1.Weight = edge01W1 * inverse;
                Count = 2;
                return;
            }

            if (edge02W0 > 0f && edge02W2 > 0f && faceW1 <= 0f)
            {
                float inverse = 1f / (edge02W0 + edge02W2);
                V0.Weight = edge02W0 * inverse;
                V2.Weight = edge02W2 * inverse;
                Count = 2;
                V1 = V2;
                return;
            }

            if (edge01W0 <= 0f && edge12W2 <= 0f)
            {
                V1.Weight = 1f;
                Count = 1;
                V0 = V1;
                return;
            }

            if (edge02W0 <= 0f && edge12W1 <= 0f)
            {
                V2.Weight = 1f;
                Count = 1;
                V0 = V2;
                return;
            }

            if (edge12W1 > 0f && edge12W2 > 0f && faceW0 <= 0f)
            {
                float inverse = 1f / (edge12W1 + edge12W2);
                V1.Weight = edge12W1 * inverse;
                V2.Weight = edge12W2 * inverse;
                Count = 2;
                V0 = V2;
                return;
            }

            float total = 1f / (faceW0 + faceW1 + faceW2);
            V0.Weight = faceW0 * total;
            V1.Weight = faceW1 * total;
            V2.Weight = faceW2 * total;
            Count = 3;
        }
    }
}
