# Capsule

The pack root for `JAG.Capsule` — everything a game's logic is written against, in one package.

Contains: `Capsule.Core`, `Capsule.Physics`, `Capsule.Scenes`.

`Capsule.Core` holds the contracts a simulation is written against: the fixed step, input, render intent, audio mixing, assets and logging.

`Capsule.Physics` holds collision and nothing else, over a `CollisionWorld2D` a headless test builds and queries directly.

`Capsule.Scenes` holds the world a game plays in: scenes, entities, components, cameras, tile maps, the screen layer a `ScreenEntity` draws an interface on, the `Focusable` and `FocusNavigator` a menu on that layer is driven by, the scene document a scene is composed from, and the input driver and `SimulationHost` a test drives it from.

Referenced by: game logic projects.

API starting points: `Capsule` (`StepContext`, `RandomSource`, `Run`), `Capsule.Scenes` (`Scene`, `Entity`, `Component`, `Camera`, `SceneSimulation`, `SimulationHost`), `Capsule.Physics` (`CollisionWorld2D`, `Shape2D`, `CollisionFilter`, `Collider2D`, `KinematicBody2D`), `Capsule.Rendering` (`FrameView`, `SpriteRenderer`, `Label`, `ColorRect`, `NineSlice`), `Capsule.Audio` (`AudioMixer`, `AudioClip`, `AudioSource`), `Capsule.Animation` (`SpriteClip`, `AnimationPlayback`, `SpriteAnimator`), `Capsule.Input` (`InputState`, `DeviceSnapshot`, `InputScript`, `IInputDriver`), `Capsule.UI` (`ScreenEntity`, `Anchor`, `Focusable`, `FocusNavigator`), `Capsule.Tiles` (`TileGrid`, `TileMap`, `TileDefinition`).

See [`docs/architecture.md`](../../docs/architecture.md) for the module map and determinism contract, [`docs/scenes.md`](../../docs/scenes.md) for scene documents, [`docs/sprite-animation.md`](../../docs/sprite-animation.md) for sheet documents, [`docs/text.md`](../../docs/text.md) for bitmap fonts, [`docs/headless-play.md`](../../docs/headless-play.md) for input drivers, and [`docs/testing.md`](../../docs/testing.md) for testing a game. Public behavior is in the XML documentation shipped beside each assembly.
