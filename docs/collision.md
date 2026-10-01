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
collider. The sample's spinning hazard keeps its box on the root and spins a child.

## Layers and filters

A collider's `Layer` names the one layer it is on. A `CollisionMask` names a set of layers. A collider's
`Detects` sets what its contacts and its own queries find. A body's `BlockedBy` sets what stops it, and
its `MovedBy` sets what carries and shoves it. Each is empty by default, and detecting a layer does not
block on it.

A game declares its layer names in one place, as `const string` fields at its assembly root, and its
masks beside them as `static readonly` fields. A query keeps a slot in the world for each mask it is
given. Build a query's mask once, never per call.

```csharp
public const string Solid = "solid";
public const string Platform = "platform";

public static readonly CollisionMask Blocking = new(Solid, Platform);
```

Every query takes the mask or filter it matches by. What a collider detects does not decide what another
query finds.

## Contacts

A collider with `ReportsContacts` set settles its contacts once a step and announces them:

```csharp
_hurtbox = new BoxCollider2D(new Vector2(hurtboxEdge, hurtboxEdge))
{
    ReportsContacts = true,
    Detects = CollisionLayers.Damaging,
};
_hurtbox.ContactEntered += OnHurtboxEntered;
```

```csharp
private void OnHurtboxEntered(ColliderContact2D contact)
{
    Health = Math.Max(Health - 1, 0);
}
```

A contact names the other side as `OtherCollider` for a collider or `Tile` for a tile map's cell, and
`OtherEntity` reaches the entity behind either. Touching includes anything within
`CollisionTolerance.ContactSkin`. In a Y-down world, standing on something gives a normal of `(0, -1)`.
`Collider2D` documents what a handler may change.

## Moving a body

`KinematicBody2D` sweeps one of its entity's colliders along a translation, stops it on what it blocks on
and slides the rest of the move along the surface:

```csharp
BoxCollider2D bodyCollider = new(Body);
Add(bodyCollider);

_body = new KinematicBody2D(bodyCollider) { BlockedBy = CollisionLayers.Blocking, Mode = BodyMode.Grounded };
Add(_body);
```

```csharp
_velocity.X = context.Input.Axis(GameInput.Move) * _tuning.WalkSpeed;
_velocity.Y += _tuning.Gravity * delta;

// IsOnFloor is state as of the last Move, so this reads the previous step's landing.
if (_body.IsOnFloor && context.Input.WasPressed(GameInput.Jump))
{
    _velocity.Y = -_tuning.JumpSpeed;
}

_body.Move(_velocity * delta);

if (_body.IsOnFloor)
{
    _velocity.Y = 0f;
}
```

`Move` returns a `MoveResult2D`, and `TestMove` moves nothing. A move stops
`CollisionTolerance.LinearSlop` short of what stopped it, within `CollisionTolerance.ContactSkin`. Its
contact then holds steady on the next step.

`BodyMode.Floating`, the default, suits a top-down game or a body with no ground. A platformer's body is
`BodyMode.Grounded`, which walks slopes. A grounded body has these levers, each documented on its member:

| Lever | What it does |
| --- | --- |
| `MaxFloorAngle` | The steepest slope that counts as a floor. |
| `KeepsHorizontalSpeedOnSlopes` | Whether a slope slows the walk's horizontal headway. |
| `StepHeight` | How tall a lip the walk climbs and how deep a drop it keeps to the floor across. |
| `RestsOnCenter` | Stands the body on its bottom center on an uneven floor instead of its lowest corner. |

### One-way surfaces

A one-way tile or a collider with `OneWay` set blocks only a body coming down onto it from above. With
`SolidSides` also set, its sides block like walls. `DropThrough` lets the next `Move` fall through:

```csharp
if (_body.IsOnFloor && context.Input.WasPressed(GameInput.Jump) && context.Input.IsHeld(GameInput.Drop))
{
    _body.DropThrough();
}
```

### Riding and shoving

A body is moved by the colliders on the layers its `MovedBy` names, and by nothing by default:

```csharp
_body.MovedBy = new(CollisionLayers.Platform);
_body.Crushed += OnCrushed;
```

A body rides such a collider when its last `Move` stopped on it, and the collider moving into the body
shoves it. A `MovedBy` layer also blocks the body. A shove that pins the body raises `Crushed`.

## Terrain

A tile map's palette declares each tile type's layer, its shape and whether it is one-way. The map
registers one `GridCollider2D` that is its own broadphase, and a map whose palette collides with nothing
registers none. A contact from a tile carries the tile map, the cell and the tile's current type.

A tile is its whole cell by default, or a convex polygon such as a slope. A tile's edge that lies flush
against a solid neighbour is no surface. A run of tiles then reads as one floor, and a slope joins the
ground beside it without a bump. A one-way tile keeps only the edges that face up, and a run of
`solidSides` tiles is walled only at its two ends.

## Queries

Every query is on the scene's world and allocates nothing. A query that finds many things writes them into
a span the caller owns. An overlap or move query returns the total count. `RaycastAll` returns how many of
the nearest hits fit:

```csharp
Span<Contact2D> contacts = stackalloc Contact2D[8];
int found = Scene.Collision.OverlapAll(Shape2D.Circle(Vector2.Zero, 24f), Position, filter, contacts);
```

| Query | Answers |
| --- | --- |
| `Raycast`, `RaycastAll` | What a ray meets. |
| `ShapeCast` | Where a shape swept along a translation first meets something. |
| `OverlapAll`, `OverlapBoxAll`, `OverlapPointAll` | Everything a shape, box or point is inside or touching. |
| `OverlapColliderAll`, `Collider2D.OverlapAll` | Everything a registered collider is touching right now. |
| `Move`, `MoveBox` | The swept move that slides along what stops it, as a floating body moves. |

Results never depend on how the broadphase happens to be arranged. The same colliders under the same
handles give the same results in the same order. `Scene.ColliderOf(hit.Target.Collider)` turns a hit back
into its `Collider2D`, or null for a tile map's cell.
