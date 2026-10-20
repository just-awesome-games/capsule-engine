# Collision

After this page you can pick the piece of Capsule's collision that answers a need. Each type's XML docs
hold its rules.

Capsule does collision only: shapes, broadphase, queries and sweeps, with no dynamics and no solver.
Velocity, gravity and friction are the game's. A controller decides a velocity each step and hands the
step's translation to a body's `Move`.

| Need | Piece |
| --- | --- |
| A shape on an entity | `BoxCollider2D`, `CircleCollider2D`, `CapsuleCollider2D` or `PolygonCollider2D` |
| A tile map that collides | `TileMapCollider2D`, with each tile type's layer and shape in the palette. It is a component, not a `Collider2D`, and its contacts arrive on the other collider as `ColliderContact2D.Tile` |
| What finds and blocks what | A collider's `Layer`, and the `CollisionMask` sets a collider's `Detects` and a body's `BlockedBy` and `MovedBy` take |
| A reaction to what a collider touches | `ReportsContacts`, with `ContactEntered`, `ContactExited` and `Touching` |
| A body that stops on what it hits | `KinematicBody2D` |
| What is where | The queries on `Scene.Collision` |
| The entity or tile a query hit | `Scene.ColliderOf(hit.Target.Collider)`, `Scene.TileOf(hit.Target)` |

A game names its layers once at its assembly root, beside the masks it shares. Every mask matches nothing
until it is set:

```csharp
public static class CollisionLayers
{
    public const string Avatar = "avatar";
    public const string Solid = "solid";

    public static readonly CollisionMask Blocking = new(Solid);
}
```

```csharp
// A trigger that reacts to the avatar.
BoxCollider2D area = new(new Vector2(32f, 32f)) { Detects = new(CollisionLayers.Avatar), ReportsContacts = true };
area.ContactEntered += contact => _occupant = contact.OtherEntity;
Add(area);

// A body that stops on solid ground.
BoxCollider2D box = new(new Vector2(12f, 16f)) { Layer = CollisionLayers.Avatar };
Add(box);
Add(new KinematicBody2D(box) { BlockedBy = CollisionLayers.Blocking });
```

A mask is immutable. `With` and `Without` return a new mask with layers added or removed. A component
adds a layer to a collider this way without wiping what its owner set:
`collider.Detects = collider.Detects.With(CollisionLayers.Wind);`. Compose a mask at setup, not in a
per-step query.

## Triggers

A trigger is a collider that reports contacts on the layers it detects, and its entity reacts in its own
handlers. It needs no layer of its own unless a query looks for it. A region that changes how something
moves, such as wind or water, is a trigger too. Its handler sets what that thing's controller reads.
