# Capsule.Scenes

The world a game plays in: a scene, the entities on it, their components, the camera, and the
scene document a scene is composed from. Substrate-free — `Capsule.Core` and
`Capsule.Collision` only — so a scene is constructible and assertable headlessly.

## Inside

- `Scene`, `Entity`, `Component`, `Camera`, `Renderer`, `QuadRenderer`, `SceneSimulation`,
  `SceneDefaults`, `DisallowMultipleComponentAttribute` — the world, its step, and the one renderer
  the engine ships. A scene owns one `CollisionWorld2D`, exposed as `Scene.Collision`.
- `SceneRegistry`, `SceneRegistration`, `SceneTransition`, `SceneDocumentAttribute` — how a scene
  is named and resolved.
- `Documents/` — the `*.scene.json` format: the document, its canonical reader and writer, and
  the placement records it holds.
- `Physics/` — `Collider2D` and its shapes (`BoxCollider2D`, `CircleCollider2D`,
  `CapsuleCollider2D`, `PolygonCollider2D`), `KinematicMover2D`, and the contact records they
  report. A collider puts an entity in the scene's collision world and raises contact enter and
  exit; a mover sweeps one of them kinematically.
- `Tiles/` — `TileGrid` and `TileDefinition`, a validated grid of palette indices, plus `TileMap`,
  the entity that draws one layer and registers its grid with the scene's collision world.
- `Spawning/` — the construction seam below the document.

`Spawning/` is where a game-defined document entry becomes an entity: a registry mapping a type string to a
constructor, the analogue of Godot's ClassDB instantiate inside a `PackedScene`. It is not a
lifecycle manager — entity CRUD is `new` plus `Scene.Add`/`Scene.Remove`.

## How it ships

Inside the `JAG.Capsule` package as its own assembly; see [`../Capsule/`](../Capsule/README.md).
Parsers for authoring formats belong in `Capsule.Cli`, never here.

## Further reading

The authoring model and the document format: [`docs/scenes.md`](../../docs/scenes.md).
