# Capsule

The pack root for `JAG.Capsule` — everything a game's logic is written against, in one package.

Contains: `Capsule.Core`, `Capsule.Collision`, `Capsule.Scenes`.

`Capsule.Core` holds the contracts a simulation is written against: the fixed step and its context, input as device snapshots and named actions, render intent, assets and logging.

`Capsule.Collision` holds collision and nothing else: shapes, the broadphase, rays, overlaps, shape casts and the axis-by-axis mover, over a `CollisionWorld2D` that a headless test builds and queries directly.

`Capsule.Scenes` holds the world a game plays in: a scene and its collision world, the entities on it, their components — colliders, kinematic bodies, sprites, animators — the camera, tile maps, the scene document a scene is composed from, and the input driver that plays it.

Referenced by: game logic projects.

API starting points: `StepContext`, `InputState`, `DeviceSnapshot` and `RandomSource` in Core; `CollisionWorld2D`, `Shape2D` and `CollisionFilter` in Collision; `Scene`, `Entity`, `Component`, `SceneSimulation`, `Camera`, `SpriteRenderer`, `Collider2D` and `IInputDriver` in Scenes.

See [`docs/architecture.md`](../../docs/architecture.md) for the module map and determinism contract, [`docs/scenes.md`](../../docs/scenes.md) for scene documents, [`docs/sprite-animation.md`](../../docs/sprite-animation.md) for sheet documents, and [`docs/headless-play.md`](../../docs/headless-play.md) for input drivers. Public behavior is in the XML documentation shipped beside each assembly.
