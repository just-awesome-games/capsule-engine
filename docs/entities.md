# Entities

After this page you know how an entity joins a scene and leaves it, when its lifecycle hooks run, how
parenting composes its transform, how interpolation avoids a visual smear, how a pause holds it, and
how to reuse an entity instead of building one every time.

## Building and adding

`new` does an entity's one-time work: attaching its components, building its sprites, reading whatever
tuning it is constructed with. `Scene.Add` places a root. A child joins through its parent and is never
added directly. An add or a remove requested during a step lands at the drain the step ends with, in the
order [`architecture.md`](architecture.md#determinism-contract) states. Until then a removed entity still
steps, collides and is found by `Scene.FindAll`. `Entity.IsRemovalPending` reads true for it and its
subtree. A component that acts on other entities checks it on both sides.

A component's `OnAttached` runs as `Entity.Add` attaches it, and `OnDetached` as `Entity.Remove` detaches
it. A component that brings its own collider or renderer attaches it in `OnAttached` and detaches it in
`OnDetached`. Parts it attaches there follow it in attachment order. Leaving a scene, a pooled return
included, detaches neither.

## Lifecycle

`OnAddedToScene` and `OnRemovedFromScene` run on every join and leave. `OnStart` runs once in an
entity's life, on its first join. A removed and re-added entity, a pooled one included, does not run it
again.

Each step, `OnStep` runs on every entity in tree order: a root, then its components in the order they
were attached, then its children the same way. Contacts then settle, and `OnLateStep` runs in that same
order. A component that must see its entity's move this step reads it in its own hook. Logic that must run
after a component goes in a later component, or in the late step when the component works in the step.
[`architecture.md`](architecture.md#determinism-contract) states the full order.

`Entity.Scene` throws while the entity is in no scene, and `Entity.SceneOrNull` reads null instead.

## Parenting

A parented entity's position, rotation and scale are local. Its world transform is the parent's applied
to them: position through the parent's scale, rotation and offset, rotation summed, scale multiplied per
axis, no shear. Renderers under the entity are placed, turned and sized by it. Colliders follow world
position alone ([`collision.md`](collision.md#colliders)). A subtree shares its root's scene, draw layer
and scroll factor, and enters and leaves the scene with it.

## Interpolation

A renderer draws its entity interpolated from the transform the step began with toward the current
one. `Teleport` moves an entity with no interpolation. An entity joining a scene also starts with none,
including a reused one.

## Pausing

`Scene.Paused` holds the world until it is cleared, and `Scene.Freeze` holds it for a count of steps.
An entity's `StepMode` decides whether it holds, and a child takes its parent's:

```csharp
Scene.Paused = true;                        // the pause menu opening
StepMode = StepMode.WhenPaused;             // the pause menu itself, which steps only while paused
Scene.Freeze(_tuning.HurtFreezeTicks);      // hitstop from a contact handler
```

## Pooling

A game that spawns and despawns entities at play rate takes a ready one from an `EntityPool<T>`. A method
called between `Take` and `Scene.Add` sets the per-life state. When that method returns the entity, a
spawn is one line:

```csharp
Scene.Add(_bolts.Take().Fire(Muzzle.WorldPosition, _visual.Facing, _bolt));
```

The engine returns the entity to its pool when it leaves and resets its own components' per-life state.
The game sets its own in `Fire`.

A pool lives as far up as its entities reach and no further. It sits on the spawner when one spawner
fires it, on the scene when several share it, and on `Run.State<T>()` when it must span scenes. A pool
builds its entities where it lives. One that lives higher than its entities reach builds them before
anything can use them. An effect that never moves is better off as one long-lived `ParticleEmitter` fed
by `Emit(count, at)`.
