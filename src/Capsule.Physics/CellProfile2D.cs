namespace Capsule.Physics;

// What one entry of a grid's palette collides as. A null layer collides as nothing. Shape is a convex
// polygon in cell space with the origin at the cell's top-left, and null is the full cell box. A
// one-way entry keeps only its up-facing edges, or with SolidSides every edge but its down-facing ones.
internal readonly record struct CellProfile2D(CollisionLayer? Layer, Shape2D? Shape = null, bool OneWay = false, bool SolidSides = false);
