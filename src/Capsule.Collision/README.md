# Capsule.Collision

Collision, and only collision: shapes, broadphase, queries, sweeps and a mover. No dynamics, no
solver, no forces. Substrate-free — `Capsule.Core` only — so a world is built and asserted
headlessly.

The module is tile-blind and entity-blind. It knows shapes, tags, collider handles and a grid of
collision kinds; what a tile means and what an entity is belong to `Capsule.Scenes`, which adapts
both onto this.

## Inside

- `CollisionWorld2D` — the seam. Everything goes through it: colliders, collision grids, tag
  interning, `Raycast`, `RaycastAll`, `ShapeCast`, `Overlap`, `OverlapCollider` and `MoveBox`.
- `Shape2D`, `ShapeKind2D`, `Aabb2D` — the fixed shape union as points and a radius: circle,
  capsule, box, convex polygon. Validated on construction and free of rotation, which stays
  render-side.
- `CollisionTag`, `CollisionFilter` — filtering by name rather than by numbered layer. Setup
  speaks strings; queries carry a 64-bit mask.
- `GridCollider2D`, `CellCollision`, `CellProfile` — one grid of collidable cells, its own
  broadphase, with per-cell tags and derived boundary faces.
- `ColliderHandle`, `CollisionTarget`, `RayHit2D`, `ShapeCastHit2D`, `Contact2D`, `MoveResult2D` —
  what a query hands back.
- `Internal/` — the narrowphase and the broadphase: GJK distance and its conservative-advancement
  shape cast, the closed-form box and ray routines that shortcut it, and the dynamic
  bounding-volume hierarchy.

## How it ships

Inside the `JAG.Capsule` package as its own assembly; see [`../Capsule/`](../Capsule/README.md).
Referenced by `Capsule.Scenes`.

## Further reading

The module map and the determinism contract:
[`docs/architecture.md`](../../docs/architecture.md). Per-member behaviour lives in the XML
comments.
