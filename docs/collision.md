# Collision

After this page you can give an entity a shape, move a body that stops on terrain, react to something
touching it, and ask the world what is where.

Capsule does collision only: shapes, broadphase, queries and sweeps, with no dynamics and no solver.
Velocity, gravity and friction are the game's.

## Colliders

A collider is a component that gives its entity a shape in the scene's `Scene.Collision` world. It
registers when the entity joins a scene, unregisters when it leaves, and follows the entity's world
position.

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

`Layer` names the layer a collider is on, and other queries' filters match that name. `SetFilter(names)`
sets what this collider's own contact queries detect. Detection does not block movement.
`KinematicBody2D.BlocksOn` owns that filter separately.

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
query finds. `Collider2D.Overlaps(other)` consults no filter, since the caller named both colliders.
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

A contact carries `Layer`, `Point`, `Normal`, and either `OtherCollider` or the grid `Cell` it reached.
`OtherEntity` reaches the entity behind either. In a Y-down world, standing on something gives a normal
of `(0, -1)`. `Touching` is everything the collider was touching as of the last step, with no budget.

A handler sees the settled state and cannot reconfigure the collider it was raised for. `Enabled`,
`Offset`, `Layer`, `ReportsContacts`, `SetFilter` and the subclass's shape throw for the length of the
dispatch. A handler may detach the collider, which takes it out of the world, gives it the exits it owes
and ends its enters for the step.

## Moving a body

`KinematicBody2D` sweeps one of its entity's colliders along a translation and stops it on what it
blocks on, one axis at a time, so stopping on one axis leaves the other free:

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

`Move` translates the entity, repopulates `MoveContacts`, and sets `IsOnFloor`, `IsOnWall`,
`IsOnCeiling`, `FloorNormal` and `WallNormal`. It returns a `MoveResult2D` carrying the translation
applied, `BlockedX` and `BlockedY`, the total `ContactCount`, and `ContactsAlongX`, how many of the
contacts the X sweep wrote. `TestMove(translation)` answers whether the move would be blocked, moving
nothing.

A move keeps `CollisionTolerance.LinearSlop` from what stopped it, and something it came to rest against
is still within `CollisionTolerance.ContactSkin` on the following step, so contact reporting stays
stable.

## Terrain

A tile map's palette declares the layer each tile type is on and which of its sides collide, and the map
registers one `GridCollider2D` that is its own broadphase ([`scenes.md`](scenes.md#the-tile-map-entry)).
A tile map whose palette collides with nothing registers no collider. A query or a body meets a tile
when its own filter names that tile's layer, and a contact from one carries the grid, the cell and the
grid's owner.

## Queries

Every query is on the scene's world, allocation-free, and writes into a span the caller owns:

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
| `Move`, `MoveBox` | The swept, axis-by-axis move `KinematicBody2D` is built on. |

An overlap or move query returns the total number of overlaps, not the number written. The span holds
the first of them in the documented order, so a count above the span's length means the rest were
counted and not written. Grid cells come first, in the order their grids were added and row-major
within each, and colliders follow by handle.

Every query takes an `ignore` handle, usually the caster's own collider. A query throws while the
collider it is on is disabled or in no scene, and a filter built from another world's layers is refused.

## Without a scene

`CollisionWorld2D` is usable on its own, and geometry is tested at that boundary. Add shapes with
`Add`, terrain with `AddGrid`, intern layers with `Layer(name)`, build filters with
`CreateFilter(names)`, then query ([`testing.md`](testing.md)).
