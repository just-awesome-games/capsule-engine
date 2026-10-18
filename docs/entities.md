# Entities

After this page you can add an entity to a scene and remove it, hook its lifecycle, parent it, pause
it, reuse it from a pool and find it again. Each type's XML docs hold its rules.

An entity is a place in a scene's world, and its components give it everything else: what it draws,
what it collides with, what it plays. The engine steps entities in a fixed order and holds them while the
scene pauses. What an entity does each step is the game's.

| Need | Piece |
| --- | --- |
| Put an entity in a scene | `Scene.Add` for a root, and `Parent` for a child |
| Take it out of the scene | `Scene.Remove(entity)`, `Scene.Remove(this)` from inside it |
| Give it a behaviour or a part | `Entity.Add` with a `Component` |
| Reach a component it holds | `Get<T>`, `TryGet<T>`, `Has<T>`, `GetInParent<T>` |
| Run code as it joins, starts, steps or leaves | `OnAddedToScene`, `OnStart`, `OnStep`, `OnLateStep`, `OnRemovedFromScene` |
| Move it with no interpolation | `Teleport` |
| Hold the world | `Scene.Paused`, `Scene.Freeze`, and each entity's `StepMode` |
| Spawn and despawn at play rate | `Scene.Pool<T>()`, declared with `assets.Pool<T>(capacity)` |
| Find another entity | `Scene.FindSingle`, `FindFirst`, `FindAll` and `Entities` |

## Building and adding

`new` does an entity's one-time work: attaching its components, building its sprites, reading whatever
tuning it is constructed with. A root is an instance of the game's own `Entity` subclass, and a plain
`new Entity(parent)` is only a marker child. `Scene.Add` places a root. A child joins through its parent
and is never added directly. An add or a remove requested during a step lands at the drain the step ends
with, in the order [`architecture.md`](architecture.md#determinism-contract) states. Until then a removed
entity still steps, collides and is found by `Scene.FindAll`. `Entity.IsRemovalPending` reads true for it
and its subtree. A component that acts on other entities checks it on both sides.

A component's `OnAttached` runs as `Entity.Add` attaches it, and `OnDetached` as `Entity.Remove` detaches
it. A component that brings its own collider or renderer attaches it in `OnAttached` and detaches it in
`OnDetached`. Parts it attaches there follow it in attachment order. Leaving a scene, a pooled return
included, detaches neither.

## Lifecycle

`OnAddedToScene` and `OnRemovedFromScene` run on every join and leave. `OnStart` runs once in an
entity's life, on its first join. A removed and re-added entity, a pooled one included, does not run it
again. Until a scene starts, an add or a removal runs no hook. The scene joins everything composed after
its assets are preloaded and before any `OnStart`.

Each step, `OnStep` runs on every entity in tree order: a root, then its components in the order they
were attached, then its children the same way. Contacts then settle, and `OnLateStep` runs in that same
order. A component that must see its entity's move this step reads it in its own hook. Logic that must run
after a component goes in a later component, or in the late step when the component works in the step.
[`architecture.md`](architecture.md#determinism-contract) states the full order.

## Parenting

A parented entity's position, rotation and scale are local. Its world transform is the parent's applied to
them: position through the parent's scale, rotation and offset, rotation summed, scale multiplied per
axis, no shear. Renderers under the entity are placed, turned and sized by it. Colliders follow world
position and refuse a turn in their ancestry. A `BoxCollider2D` also follows an axis-aligned scale, and
every other collider refuses one. A subtree shares its root's scene, draw layer and scroll factor, and
enters and leaves the scene with it. A rotation in code is radians, clockwise on screen. A scene
document's `rotation`, the particle angles and `KinematicBody2D.MaxFloorAngle` are degrees.

## Interpolation

A renderer draws its entity interpolated from the transform the step began with toward the current
one. An entity joining a scene starts with no interpolation, a pooled one included.

## Pausing

An entity's `StepMode` decides whether a pause or a freeze holds it, and a child takes its parent's:

```csharp
Scene.Paused = true;               // the pause menu opening
StepMode = StepMode.WhenPaused;    // in the pause menu's constructor: it steps only while paused
```

## Pooling

A game that spawns and despawns entities at play rate takes a ready one from its scene's pool of the type. The
spawner declares the pool in `CollectAssets` and takes from `Scene.Pool<T>()`. A method called between `Take`
and `Scene.Add` sets the per-life state. Here `Ripple` is a game's short-lived effect whose `At` sets where it
plays and returns it:

```csharp
protected override void CollectAssets(AssetCollection assets) => assets.Pool<Ripple>(capacity: 4);

Scene.Add(Scene.Pool<Ripple>().Take().At(Position));
```

The engine returns the entity to its pool when it leaves and resets its own components' per-life state.

A pooled type builds with no arguments. Each kind of pooled entity is a type of its own, which the scene's
pool is found by and preloads. Two effects that differ only in their clip are two small types. A spawner
holds its own `new EntityPool<T>(create, capacity)` only where a type cannot name the pool: its entities take
constructor arguments, or one pool of a base type is filled by several factories. It forwards that pool from
its `CollectAssets`. A pool that must span scenes lives on `Run.State<T>()` and builds its entities where it
lives. An effect that never moves is better off as one long-lived `ParticleEmitter` fed by `Emit(count, at)`.

## Finding entities

`FindSingle`, `FindFirst` and `FindAll` walk `Scene.Entities` in step order and match every entity
assignable to the type asked for, an interface included. `FindAll` returns a walk for `foreach`, not a
collection, and allocates nothing. A count is a loop over it.

```csharp
protected override void OnStart()
{
    _door = Scene.FindSingle<Door>();

    foreach (Lamp lamp in Scene.FindAll<Lamp>())
    {
        lamp.Color = ColorRgba.Black;
    }
}
```
