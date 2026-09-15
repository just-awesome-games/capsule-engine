# Capsule

The pack root for `JAG.Capsule`: everything a game's logic is written against, in one package.

Contains `Capsule.Core` (the contracts a simulation is written against: the fixed step, input, render intent, audio mixing, assets and logging), `Capsule.Physics` (collision and nothing else, over a `CollisionWorld2D` a headless test builds and queries directly), and `Capsule.Scenes` (the world a game plays in: scenes, entities, components, cameras, tile maps, the screen layer, the scene document a scene is composed from, and the input driver and `SimulationHost` a test drives it from).

Referenced by game logic projects. Public behaviour is the XML documentation shipped beside each assembly; [`docs/architecture.md`](../../docs/architecture.md) holds the module map and determinism contract, [`docs/scenes.md`](../../docs/scenes.md), [`docs/sprite-animation.md`](../../docs/sprite-animation.md) and [`docs/text.md`](../../docs/text.md) the document formats, [`docs/headless-play.md`](../../docs/headless-play.md) and [`docs/testing.md`](../../docs/testing.md) driving and testing a game.
