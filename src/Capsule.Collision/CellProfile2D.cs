namespace Capsule.Collision;

/// <summary>What one entry of a grid's palette collides as.</summary>
/// <param name="Layer">
/// The layer every cell of this entry is on. A null layer contributes no cell at all: the cell is
/// empty as far as collision is concerned.
/// </param>
/// <param name="Faces">Which sides of the cell collide; every side by default.</param>
public readonly record struct CellProfile2D(CollisionLayer? Layer, CellFaces2D Faces = CellFaces2D.All);
