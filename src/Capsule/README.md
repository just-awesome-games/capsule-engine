# Capsule

The pack root for `JAG.Capsule` — everything a game's logic is written against, in one package.

Contains: `Capsule.Core`, `Capsule.Collision`, `Capsule.Scenes`.

`Capsule.Core` holds the contracts a simulation is written against: the fixed step and its context, input as named actions, render intent, assets and logging.

`Capsule.Collision` holds collision and nothing else: shapes, the broadphase, rays, overlaps, shape casts and the axis-by-axis mover, over a `CollisionWorld2D` that a headless test builds and queries directly.

`Capsule.Scenes` holds the world a game plays in: a scene and its collision world, the entities on it, their components — colliders, kinematic bodies, sprites, animators — the camera, tile maps, and the scene document a scene is composed from.

Referenced by: game logic projects.

See [`docs/architecture.md`](../../docs/architecture.md) for the module map and determinism contract.
