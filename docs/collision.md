# Collision

After this page you can give an entity a shape, move a body that stops on terrain, react to something
touching it, and ask the world what is where.

Capsule does collision only: shapes, broadphase, queries and sweeps, with no dynamics and no solver.
Velocity, gravity and friction are the game's.

## Colliders

A collider is a component that gives its entity a shape in the scene's `Scene.Collision` world. It
registers when the entity joins a scene and unregisters when the entity leaves.

| Collider | Shape |
| --- | --- |
| `BoxCollider2D(size)` | An axis-aligned box, its corner at the entity's position plus `Offset`. |
| `CircleCollider2D(radius)` | A circle centred on the entity's position plus `Offset`. |
| `CapsuleCollider2D(start, end, radius)` | A segment swollen by a radius. |
| `PolygonCollider2D(points, radius)` | A convex polygon, optionally rounded. |

A collider follows position alone. An entity turned or scaled anywhere up its ancestry refuses a
collider, and an entity refuses a turn or a scale while a collider sits beneath it. The sample's
spinning hazard keeps its box on the root and spins a child.

## Layers and filters

A collider's `Layer` names the one layer it is on. A `CollisionMask` names a set of layers, and three
properties take one: a collider's `Detects` sets what its contacts and its own queries find, a body's
`BlockedBy` sets what stops it, and its `MovedBy` sets what carries and shoves it. A `MovedBy` layer
also stops the body. Each is empty by default, and detecting a layer does not block on it.

```csharp
_hurtbox = new BoxCollider2D(new Vector2(hurtboxEdge, hurtboxEdge))
{
    Offset = new Vector2(_tuning.HurtboxInset, _tuning.HurtboxInset),
    ReportsContacts = true,
    Detects = CollisionLayers.Damaging,
};
```

A game declares its layer names in one place, as `const string` fields at its assembly root, and its
masks beside them as `static readonly` fields. A set used once can be written in place, as
`Detects = new(CollisionLayers.Player)`. A world interns up to `CollisionWorld2D.MaxLayers` names, each
the first time a mask naming it reaches the world.

```csharp
public const string Solid = "solid";
public const string Platform = "platform";

public static readonly CollisionMask Blocking = new(Solid, Platform);
```

Every query takes the mask or filter it matches by, and what a collider detects does not decide what
another query finds. `Collider2D.Overlaps(other)` consults no filter.
`CollisionFilter.None` and `CollisionFilter.Everything` name no layer table and are accepted by any
world. A query keeps a slot in the world for each mask it is given, so a query's mask is a
`static readonly` field and never built per call. A mask only `Detects`, `BlockedBy` and `MovedBy`
use takes no slot.

## Contacts

A collider with `ReportsContacts` set settles its contacts once a step and announces them:

```csharp
_hurtbox.ContactEntered += OnHurtboxEntered;
_hurtbox.ContactExited += OnHurtboxExited;
```

```csharp
private void OnHurtboxEntered(ColliderContact2D contact)
{
    Health = Math.Max(Health - 1, 0);
    Log.Info(FormattableString.Invariant($"entered {contact.LayerName} at {contact.Point}, health {Health}"));
}
```

A contact names the other side as `OtherCollider` for a collider or `Tile` for a tile map's cell, and
`OtherEntity` reaches the entity behind either. Touching includes anything within
`CollisionTolerance.ContactSkin`, and two colliders sharing an edge with no gap touch. In a Y-down
world, standing on something gives a normal of `(0, -1)`. `Touching` is everything the collider was
touching as of the last step.

A handler sees the settled state. `Collider2D` documents what a handler may and may not change.

## Moving a body

`KinematicBody2D` sweeps one of its entity's colliders along a translation and stops it on what it
blocks on. It slides the rest of the move along whatever stopped it, so a body pressed against a wall
keeps falling:

```csharp
BoxCollider2D bodyCollider = new(Body);
Add(bodyCollider);

_body = new KinematicBody2D(bodyCollider) { BlockedBy = CollisionLayers.Blocking };
Add(_body);
```

```csharp
_velocity.X = context.Input.Axis(GameInput.Move) * _tuning.WalkSpeed;
_velocity.Y += _tuning.Gravity * delta;

// IsOnFloor is state as of the last Move, so this reads the previous step's landing.
bool wasOnFloor = _body.IsOnFloor;
if (wasOnFloor && context.Input.WasPressed(GameInput.Jump))
{
    _velocity.Y = -_tuning.JumpSpeed;
}

_body.Move(_velocity * delta);

if (_body.IsOnFloor)
{
    _velocity.Y = 0f;
}
```

`Move` reports the surfaces it reached on the body and returns a `MoveResult2D`. `TestMove` answers
whether a move would be blocked and moves nothing.

A move reports every surface it meets at the same moment. That is the nearest surface and any other
within a ten-thousandth of the move's length of it. A box landing across a run of tiles reports each
tile under it.

A move stops `CollisionTolerance.LinearSlop` short of what stopped it. A surface it came to rest against
is still within `CollisionTolerance.ContactSkin` on the following step, and its contact holds steady.

### Slopes

A body's `Mode` decides how it meets a slope. `BodyMode.Floating`, the default, suits a top-down game or
a body with no ground. A platformer's body is `BodyMode.Grounded`:

```csharp
_body = new KinematicBody2D(bodyCollider) { BlockedBy = CollisionLayers.Blocking, Mode = BodyMode.Grounded };
```

A grounded walk on a slope covers the move's whole X horizontally. The slope sets how far the body
rises or falls. A climb and a descent make the same horizontal headway as flat ground. When
`KeepsHorizontalSpeedOnSlopes` is false, the walk covers the move's X along the slope's surface
instead. A steeper slope then gives less horizontal headway. A game that wants a climb to feel heavy
scales its own speed from `FloorNormal`.

### Steps

A grounded body's `StepHeight` lets its walk climb a lip and keep to a floor below a drop. It is 0 by
default, which turns stepping off:

```csharp
_body = new KinematicBody2D(bodyCollider) { BlockedBy = CollisionLayers.Blocking, Mode = BodyMode.Grounded, StepHeight = 4f };
```

A body that stood on a floor and walks into a wall rises by up to `StepHeight`. It walks on with the
rest of the move and settles onto a floor no lower than where it started. That floor sets
`FloorNormal` and what the body rides. The step keeps the move's whole X. The wall stops the body as
usual when there is no room to rise or no floor to settle on. A body that walks off a drop no deeper
than `StepHeight` below where its walk ended stays on the lower floor without an airborne step. A move
that rises never steps, and neither does one after `DropThrough`. A body steps onto a one-way surface
only where that surface blocks it. It walks through a plain one-way lip.

Only a walk stopped by a wall pays for a step. A wall met higher than `StepHeight` above the body's
lowest point is ruled out at once. Any other wall costs a rise, a walk and a settle, each one sweep.
Keeping to a lower floor costs one more sweep, and only after a walk has left its floor.

### Standing on the center

A box on a slope rests on its uphill corner, and its bottom center hangs above the floor. A grounded
body with `RestsOnCenter` stands with its bottom center on the floor instead:

```csharp
_body = new KinematicBody2D(bodyCollider) { BlockedBy = CollisionLayers.Blocking, Mode = BodyMode.Grounded, RestsOnCenter = true };
```

It matters only on an uneven floor. On a flat floor the body stands where it would anyway. The body
still sweeps the pose it would hold with this off, and the entity stands below that pose by up to half
the collider's width times the tangent of `MaxFloorAngle`. Walls, ceilings, steps, `TestMove`, carries
and shoves all meet the swept pose, so each behaves exactly as it does with this off. A downhill walk
also casts the hanging half of the box ahead, and a wall it meets stops the move. A landing, or any
move that would lower the entity further below the swept pose, first sweeps that half for a wall and
keeps the entity's height when it finds one. `FloorNormal` reports the floor under the center. Setting the entity's position directly clears the sink, and the
next grounded move finds it again.

At a ledge the body keeps its height until its whole box has left the ledge. It never sinks over the
edge, including the edge of a one-way floor. A body that steps up onto a ledge stands on it at once.

A grounded move casts one ray down from the bottom center. A move over a change of slope casts a
second ray from that floor to the corner the box rests on, and a downhill walk casts one shape. A move
that lowers the entity further below the swept pose casts one more shape. A change of slope on a
one-way floor casts up to three more rays down. A flat floor costs the one ray. A body with
`RestsOnCenter` off pays nothing.

A crest followed within half a width by a step, or a short rise between two flat floors, can leave the
body a little high or low. It never ends inside a wall or below the floor under its center. A round
collider walking downhill stops slightly short of a steep wall, because the cast ahead is a box.

### One-way surfaces

A one-way tile or a collider with `OneWay` set blocks only a body coming down onto it from above, and
only one that started clear of it. A body jumps up through it and walks through it sideways.
With `SolidSides` (`solidSides` in a palette) also set, its sides block like walls, and a run of such
tiles is walled only at its two ends.
`DropThrough` lets the next `Move` fall through it:

```csharp
if (wasOnFloor && context.Input.WasPressed(GameInput.Jump))
{
    if (context.Input.IsHeld(GameInput.Drop))
    {
        _body.DropThrough();
    }
    else
    {
        _velocity.Y = -_tuning.JumpSpeed;
    }
}
```

### Riding and shoving

A body is moved by the colliders on the layers its `MovedBy` names, and by nothing by default:

```csharp
_body.MovedBy = new(CollisionLayers.Platform);
_body.Crushed += OnCrushed;
```

A body rides such a collider when its last `Move` stopped on it, and such a collider moving into the
body shoves it. A `MovedBy` layer also blocks the body, whether or not `BlockedBy` names it. A
collider moving into several bodies shoves them one at a time, in the order of their
handles. A shove that pins the body against something it cannot pass raises `Crushed`.

## Terrain

A tile map's palette declares the layer each tile type is on, its shape and whether it is one-way, and the
map registers one `GridCollider2D` that is its own broadphase ([`scenes.md`](scenes.md#the-tile-map-entry)).
A tile map whose palette collides with nothing registers no collider. A query or a body meets a tile
when its own filter names that tile's layer, and a contact from one carries the tile map, the cell and
the tile's current type.

A tile is its whole cell by default, or a convex polygon of three or four points such as a slope. A
tile's edge that lies flush against a solid neighbour is no surface, so a run of tiles reads as one
floor and a slope joins the ground beside it without a bump. A one-way tile keeps only the edges that
face up.

## Queries

Every query is on the scene's world and allocates nothing. A query that finds many things writes them
into a span the caller owns:

```csharp
Span<Contact2D> contacts = stackalloc Contact2D[8];
int found = Scene.Collision.OverlapAll(Shape2D.Circle(Vector2.Zero, 24f), Position, filter, contacts);
```

| Query | Answers |
| --- | --- |
| `Raycast` | The first thing a ray meets, or nothing. |
| `RaycastAll` | The nearest hits, nearest first. The span is both the budget and the destination. |
| `ShapeCast` | Where a shape swept along a translation first meets something. |
| `OverlapAll`, `OverlapBoxAll` | Everything a shape or box is inside or touching. |
| `OverlapPointAll` | Everything that contains a point or has it on an edge. |
| `OverlapColliderAll`, `Collider2D.OverlapAll` | Everything a registered collider is touching right now. |
| `Move`, `MoveBox` | The swept move that slides along what stops it, as a floating body moves. |

An overlap or move query returns the total number of overlaps, not the number written. The span holds
the first of them in the documented order. A count above the span's length means the rest were counted
and not written. Grid cells come first, in the order their grids were added and row-major within each,
and colliders follow by handle.

No query, move or shove depends on how the broadphase happens to be arranged. The same colliders under
the same handles give the same results in the same order.

`Scene.ColliderOf(hit.Target.Collider)` turns any hit back into its `Collider2D` and its `Entity`, and
answers null for a tile map's cell.

A world query takes an `ignore` handle, usually the caster's own collider. A query on a collider throws
while that collider is disabled or in no scene. A filter built from another world's layers is refused.
