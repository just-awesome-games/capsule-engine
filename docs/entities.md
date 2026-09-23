# Entities

After this page you know how an entity joins a scene and leaves it, when its lifecycle hooks run, how
parenting composes its transform, how interpolation avoids a visual smear, how a pause holds it, and
how to reuse an entity instead of building one every time.

## Building and adding

`new` does an entity's one-time work: attaching its components, building its sprites, reading whatever
tuning it is constructed with. `Scene.Add` places a root; a child already has a parent and joins through
it, so only a root is ever added directly. A structural change requested during a step, an add or a
remove, lands at the drain the step ends with, in the order
[`architecture.md`](architecture.md#determinism-contract) states.

## Lifecycle

`OnAddedToScene` and `OnRemovedFromScene` run on every join and leave. `OnStart` runs once per entity
ever, on its first join, and a removed then re-added entity does not run it again.

`Entity.Scene`, `Component.Run` and `Camera.Scene` are non-null accessors and throw before the object is
in a started scene, so an entity can write `Scene.Add(...)` from its own step. Read `SceneOrNull` when an
entity may not be in a scene yet.

## Parenting

A parented entity's position, rotation and scale are local. Its world transform is the parent's applied
to them: position through the parent's scale, rotation and offset, rotation summed, scale multiplied per
axis, no shear. The transform is cached and recomposed lazily after a write anywhere above. Renderers
under the entity are placed, turned and sized by it, interpolated from the transform the step began with.
Colliders follow world position alone and refuse a turned or scaled ancestry. A subtree shares its root's
scene, draw layer and scroll factor, and enters and leaves the scene with it.

## Interpolation

`PreviousTransform` is what a renderer interpolates from toward the current transform, and the engine
saves it at the top of every step. `Teleport` moves an entity with no interpolation, collapsing
`PreviousTransform` onto the position it moved to. An entity joining a scene collapses the same way, so a
freshly placed or a reused entity never smears in from wherever it stood before.

## Pausing

`Scene.Paused` holds the world until it is cleared, and `Scene.Freeze` holds it for a count of steps.
An entity's `StepMode` decides whether it holds, and a child takes its parent's:

```csharp
Scene.Paused = true;                        // the pause menu opening
StepMode = StepMode.WhenPaused;             // the pause menu itself, which steps only while paused
Scene.Freeze(_tuning.HurtFreezeTicks);      // hitstop from a contact handler
```

## Pooling

A game that spawns and despawns entities at play rate reuses a ready one instead of `new`-ing it.
`EntityPool<T>` builds a fixed set up front and hands one out with `Take` or `TryTake`. The method
between `Take` and `Scene.Add` sets the per-life state. Have it return the entity and a spawn is one line:

```csharp
Scene.Add(_bolts.Take().Fire(Muzzle.WorldPosition, _visual.Facing, _bolt));
```

The game never writes a release call. The engine returns the entity to its pool when `Scene.Remove` lands
or the scene stops. The engine resets its own components' per-life state when an entity leaves, and the
game sets its own in `Fire`.

`Take` grows the pool past its built capacity instead of failing, and logs once that it did; `TryTake`
refuses instead, and the game decides what an empty pool means. A pool lives as far up as its entities
reach and no further: on the spawner when one spawner fires it, on the scene when several share it, on
`Run.State<T>()` when it must span scenes. A pool prewarms where it lives, and one that lives higher than
its entities reach builds them before anything can use them. An effect that never moves is better off as
one long-lived `ParticleEmitter` fed by `Emit(count, at)`.
