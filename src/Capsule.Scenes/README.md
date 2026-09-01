# Capsule.Scenes

The world a game plays in: a scene, the entities on it, their components, the camera, and the
scene document a scene is composed from. Substrate-free — `Capsule.Core` and
`Capsule.Collision` only — so a scene is constructible and assertable headlessly.

## Inside

- `Scene`, `Entity`, `Component`, `Camera`, `Renderer`, `SceneSimulation`, `SceneDefaults` — the
  world and its step. A scene owns one `CollisionWorld`, exposed as `Scene.Collision`.
- `SceneRegistry`, `SceneRegistration`, `SceneTransition`, `SceneDocumentAttribute` — how a scene
  is named and resolved.
- `Documents/` — the `*.scene.json` format: the document, its canonical reader and writer, and
  the placement records it holds.
- `Tiles/` — `TileGrid` and `TileDefinition`: a validated grid of palette indices. Tile-map data
  with no behaviour, read from above by the document and by `Entities/TileMap`.
- `Entities/`, `Components/`, `Spawning/` — the entities and components the engine itself ships,
  and the construction seam below. `Collider` puts an entity in the scene's collision world, moves
  it kinematically, and raises contact enter and exit; `TileMap` registers its layer's grid there.

`Spawning/` is where a game-defined document entry becomes an entity: a registry mapping a type string to a
constructor, the analogue of Godot's ClassDB instantiate inside a `PackedScene`. It is not a
lifecycle manager — entity CRUD is `new` plus `Scene.Add`/`Scene.Remove`.

## How it ships

Inside the `JAG.Capsule` package as its own assembly; see [`../Capsule/`](../Capsule/README.md).
Parsers for authoring formats belong in `Capsule.Cli`, never here.

## Further reading

The authoring model and the document format: [`docs/scenes.md`](../../docs/scenes.md).
