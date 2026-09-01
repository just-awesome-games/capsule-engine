using System.Numerics;

namespace Capsule.Collision;

/// <summary>Where a ray first met something.</summary>
/// <param name="Target">What it met.</param>
/// <param name="Point">The world-space point of first contact.</param>
/// <param name="Normal">The unit surface normal at that point, pointing back along the ray.</param>
/// <param name="Distance">World units from the ray's origin to <paramref name="Point"/>.</param>
public readonly record struct RayHit(CollisionTarget Target, Vector2 Point, Vector2 Normal, float Distance);

/// <summary>Where a swept shape first met something.</summary>
/// <param name="Target">What it met.</param>
/// <param name="Point">The world-space point of first contact.</param>
/// <param name="Normal">The unit surface normal at that point, pointing back against the sweep.</param>
/// <param name="Fraction">
/// How far along the translation the sweep reached, in [0, 1]. Zero means the shape was already
/// touching before it moved.
/// </param>
public readonly record struct ShapeCastHit(CollisionTarget Target, Vector2 Point, Vector2 Normal, float Fraction);

/// <summary>Something a collider is touching.</summary>
/// <param name="Target">What is being touched.</param>
/// <param name="Point">A world-space point on the touched surface.</param>
/// <param name="Normal">
/// The unit surface normal at the contact, pointing from the target back towards the collider.
/// </param>
public readonly record struct Contact(CollisionTarget Target, Vector2 Point, Vector2 Normal);
