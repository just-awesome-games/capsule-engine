# Entities

After this page you know how an entity joins a scene and leaves it, when its lifecycle hooks run, how
parenting composes its transform, how interpolation avoids a visual smear, how a pause holds it, and
how to reuse an entity instead of building one every time.

## Building and adding

`new` does an entity's one-time work: attaching its components, building its sprites, reading whatever
tuning it is constructed with. `Scene.Add` places a root. A child joins through its parent and is never
added directly. An add or a remove requested during a step lands at the drain the step ends with, in the
order [`architecture.md`](architecture.md#determinism-contract) states.

## Lifecycle

`OnAddedToScene` and `OnRemovedFromScene` run on every join and leave. `OnStart` runs once in an
entity's life, on its first join. A removed and re-added entity, a pooled one included, does not run it
again.

`Entity.Scene`, `Component.Run` and `Camera.Scene` never return null. Each throws until its object is in
a scene, and a `Run` accessor also throws until the scene has started. Read `Entity.SceneOrNull` when an
entity may not be in a scene yet.

## Parenting

A parented entity's position, rotation and scale are local. Its world transform is the parent's applied
to them: position through the parent's scale, rotation and offset, rotation summed, scale multiplied per
axis, no shear. Renderers under the entity are placed, turned and sized by it. Colliders follow world
position alone ([`collision.md`](collision.md#colliders)). A subtree shares its root's scene, draw layer
and scroll factor, and enters and leaves the scene with it.

## Interpolation

A renderer draws its entity interpolated from the transform the step began with toward the current
one. `Teleport` moves an entity with no interpolation. An entity joining a scene also starts with none,
and a freshly placed or reused entity never smears in from where it stood before.

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
`EntityPool<T>` builds a set up front and hands one out with `Take` or `TryTake`. A method called between
`Take` and `Scene.Add` sets the per-life state. When that method returns the entity, a spawn is one line:

```csharp
Scene.Add(_bolts.Take().Fire(Muzzle.WorldPosition, _visual.Facing, _bolt));
```

The game never writes a release call. The engine resets its own components' per-life state when an
entity leaves, and the game sets its own in `Fire`.

A pool lives as far up as its entities reach and no further. It sits on the spawner when one spawner
fires it, on the scene when several share it, and on `Run.State<T>()` when it must span scenes. A pool
builds its entities where it lives. One that lives higher than its entities reach builds them before
anything can use them. An effect that never moves is better off as one long-lived `ParticleEmitter` fed
by `Emit(count, at)`.
