# Navigation

After this page you can move an entity along a path of points, such as a floor that travels and carries
what stands on it. Each type's XML docs hold its rules.

## Paths

A `Path2D` is straight segments through two or more points, measured by distance along it. It never
changes once built, and any number of entities share one. A path whose last point repeats its first is
`Closed`, and runs around a loop:

```csharp
Path2D track = new(Vector2.Zero, new Vector2(48f, 0f));
Path2D loop = new(Vector2.Zero, new Vector2(32f, 0f), new Vector2(32f, 32f), Vector2.Zero);
```

A scene document writes a path as its points. An `[Authorable] Path2D` member reads them:

```json
{ "type": "slab", "x": 184, "y": 168, "track": [[0, 0], [48, 0]] }
```

A `PathFollower2D` moves its entity along a path at `Speed` units a second. The path and the speed are in
the space of the entity's `Position`, its parent's, and the points are relative to where the entity stands
as the follower starts. `Loop` says what happens at the end: `LoopMode.Once`
holds there and reads `IsFinished`, `Repeat` starts over, and `PingPong` runs back. `Repeat` on a path
that is not closed jumps from the last point to the first.

```csharp
Add(new PathFollower2D(track) { Speed = 48f, Loop = track.Closed ? LoopMode.Repeat : LoopMode.PingPong });
```

The follower moves the entity by writing its position. A collider on the entity then carries each body
riding it whose `MovedBy` names the collider's layer ([`collision.md`](collision.md)). `Offset` is the
distance along the path, and writing it places the entity there. Motion other than a steady speed writes
`Offset` from the game, such as a `Tween` easing each leg with `Speed` left at zero.
