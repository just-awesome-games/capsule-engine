# Collision

After this page you can pick the piece of Capsule's collision that answers a need. Each type's XML docs
hold its rules.

Capsule does collision only: shapes, broadphase, queries and sweeps, with no dynamics and no solver.
Velocity, gravity and friction are the game's. A controller decides a velocity each step and hands the
step's translation to a body's `Move`.

| Need | Piece |
| --- | --- |
| A shape on an entity | `BoxCollider2D`, `CircleCollider2D`, `CapsuleCollider2D` or `PolygonCollider2D` |
| A tile map that collides | `TileMapCollider2D`, with each tile type's layer and shape in the palette |
| What finds and blocks what | A collider's `Layer`, and the `CollisionMask` sets a collider's `Detects` and a body's `BlockedBy` and `MovedBy` take |
| A reaction to what a collider touches | `ReportsContacts`, with `ContactEntered`, `ContactExited` and `Touching` |
| A body that stops on what it hits | `KinematicBody2D` |
| What is where | The queries on `Scene.Collision` |

## Triggers

A trigger is a collider that reports contacts on the layers it detects, and its entity reacts in its own
handlers. It needs no layer of its own unless a query looks for it. A region that changes how something
moves, such as wind or water, is a trigger too. Its handler sets what that thing's controller reads.
