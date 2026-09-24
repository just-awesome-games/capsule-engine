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

A collider's `Layer` names the layer it is on, and other queries' filters match that name. Its
`SetFilter(names)` sets what its own contacts detect. Detection does not block movement.
`KinematicBody2D.BlocksOn` sets what blocks a body.

```csharp
_hurtbox = new BoxCollider2D(new Vector2(hurtboxEdge, hurtboxEdge))
{
    Offset = new Vector2(_tuning.HurtboxInset, _tuning.HurtboxInset),
    ReportsContacts = true,
};
_hurtbox.SetFilter(CollisionLayers.Damaging);
```

A game declares its layer names in one place, as `const string` fields at its assembly root, and passes
them by name. A world interns up to `CollisionWorld2D.MaxLayers` names.

Every query takes the filter it matches by, and a collider's own filter does not decide what another
query finds. `Collider2D.Overlaps(other)` consults no filter.
`CollisionFilter.None` and `CollisionFilter.Everything` name no layer table and are accepted by any
world.

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
`OtherEntity` reaches the entity behind either. In a Y-down world, standing on something gives a normal
of `(0, -1)`. `Touching` is everything the collider was touching as of the last step.

A handler sees the settled state. `Collider2D` documents what a handler may and may not change.

## Moving a body

`KinematicBody2D` sweeps one of its entity's colliders along a translation and stops it on what it
blocks on. It slides the rest of the move along whatever stopped it, so a body pressed against a wall
keeps falling:

```csharp
BoxCollider2D bodyCollider = new(Body);
Add(bodyCollider);

_body = new KinematicBody2D(bodyCollider);
_body.BlocksOn(CollisionLayers.Blocking);
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

A move stops `CollisionTolerance.LinearSlop` short of what stopped it. A surface it came to rest against
is still within `CollisionTolerance.ContactSkin` on the following step, and its contact holds steady.

### Slopes

A body's `Mode` decides how it meets a slope. `BodyMode.Floating`, the default, suits a top-down game or
a body with no ground. A platformer's body is `BodyMode.Grounded`:

```csharp
_body = new KinematicBody2D(bodyCollider) { Mode = BodyMode.Grounded };
```

A game that wants a climb to feel heavy scales its own speed from `FloorNormal`.

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

A body is moved by the colliders on the layers it names, and by nothing by default:

```csharp
_body.MovedBy(CollisionLayers.Platform);
_body.Crushed += OnCrushed;
```

A body rides such a collider when its last `Move` stopped on it, and such a collider moving into the
body shoves it. A shove that pins the body against something it cannot pass raises `Crushed`.

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
| `OverlapColliderAll`, `Collider2D.OverlapAll` | Everything a registered collider is touching right now. |
| `Move`, `MoveBox` | The swept move that slides along what stops it, as a floating body moves. |

An overlap or move query returns the total number of overlaps, not the number written. The span holds
the first of them in the documented order. A count above the span's length means the rest were counted
and not written. Grid cells come first, in the order their grids were added and row-major within each,
and colliders follow by handle.

A world query takes an `ignore` handle, usually the caster's own collider. A query on a collider throws
while that collider is disabled or in no scene. A filter built from another world's layers is refused.
