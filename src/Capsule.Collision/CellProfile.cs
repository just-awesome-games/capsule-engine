namespace Capsule.Collision;

/// <summary>
/// What one entry of a grid's palette collides as. The tag is a name the world interns so query
/// results retain the authored identity of every cell.
/// </summary>
/// <param name="Collision">The shape this palette entry contributes, if any.</param>
/// <param name="Tag">The name every cell of this entry carries; blank means untagged.</param>
public readonly record struct CellProfile(CellCollision Collision, string? Tag = null);
