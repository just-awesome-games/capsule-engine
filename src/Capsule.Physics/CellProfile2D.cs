namespace Capsule.Physics;

/// <summary>What one entry of a grid's palette collides as.</summary>
/// <param name="Layer">The layer cells of this entry are on. A null layer collides as nothing.</param>
/// <param name="Faces">Which sides of the cell collide. Every side by default.</param>
public readonly record struct CellProfile2D(CollisionLayer? Layer, CellFaces2D Faces = CellFaces2D.All);
