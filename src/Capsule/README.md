# Capsule

The pack root for `JAG.Capsule` — everything a game's logic is written against, in one package.

Contains: `Capsule.Core`, `Capsule.Collision`, `Capsule.Scenes`.

`Capsule.Core` holds the contracts a simulation is written against: the fixed step, input, render intent, audio mixing, assets and logging.

`Capsule.Collision` holds collision and nothing else, over a `CollisionWorld2D` a headless test builds and queries directly.

`Capsule.Scenes` holds the world a game plays in: scenes, entities, components, cameras, tile maps, the scene document a scene is composed from, and the input driver and `SceneRun` a test drives it from.

Referenced by: game logic projects.

API starting points: `StepContext`, `InputState`, `DeviceSnapshot`, `RandomSource`, `AudioMixer` and `AudioClip` in Core; `CollisionWorld2D`, `Shape2D` and `CollisionFilter` in Collision; `Scene`, `Entity`, `Component`, `SceneSimulation`, `SceneRun`, `Camera`, `SpriteRenderer`, `Label`, `AudioSource`, `Collider2D` and `IInputDriver` in Scenes.

See [`docs/architecture.md`](../../docs/architecture.md) for the module map and determinism contract, [`docs/scenes.md`](../../docs/scenes.md) for scene documents, [`docs/sprite-animation.md`](../../docs/sprite-animation.md) for sheet documents, [`docs/text.md`](../../docs/text.md) for bitmap fonts, [`docs/headless-play.md`](../../docs/headless-play.md) for input drivers, and [`docs/testing.md`](../../docs/testing.md) for testing a game. Public behavior is in the XML documentation shipped beside each assembly.
