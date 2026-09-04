# Project layout

Capsule suggests one directory convention for a game's logic assembly. The folder set is Capsule's own vocabulary and nothing else:

`Scenes/`, `Entities/`, `Components/`, `Cameras/`

That is the whole list. A consumer derives it from the engine's API rather than inventing one, which is the property that makes a convention survive contact with many hands.

- A concept gets its folder as soon as it has one file. There is no file-count threshold and no promotion judgement to get wrong, because the vocabulary is closed and small.
- A concept with no files gets no folder. An empty folder is an instruction to fill it.
- Nest inside a concept folder only when it becomes genuinely crowded, and nest by game domain — `Entities/Enemies/` — never one folder per class: a Capsule entity is code alone, with its assets under `src/asset-sources/` — its art in `textures/`, its animation in `sprites/` — so a folder per entity would hold one file indefinitely.
- Folders map to namespaces: `Entities/Player.cs` declares `MyGame.Game.Entities`.
- That namespace is also the registry key the type claims, so where an entity or a scene is filed is what a document names it by. The key is the namespace under the assembly's root namespace, minus the leading `Entities` or `Scenes` segment and minus a trailing segment repeating the type's own name, kebab-cased per segment and joined with `/`: `Entities/Player.cs` claims `player`, `Entities/Enemies/Bat.cs` claims `enemies/bat`, and `Scenes/Stage1/Room01.cs` composes `asset-sources/scenes/stage-1/room-01.scene.json`. Two classes of one name in different folders are two keys.

The assembly root holds the game's declarations — its collision layer names, input actions, world units and similar. Those are the vocabulary everything else references and sit at the top of the dependency graph; they are not contents of the game the way a scene or an entity is. Root is what the game *is*; folders are what it *contains*.

## Worked example

[`samples/MinimalGame/`](../samples/MinimalGame/) is laid out this way:

```text
src/MinimalGame.Game/
  GameInput.cs  World.cs
  Scenes/    MainMenu.cs  Room.cs
  Entities/  Player.cs  Sensor.cs
  Cameras/   GameCamera.cs
src/MinimalGame.Shell/
  Program.cs
```

`GameInput.cs` is the game's action vocabulary and `World.cs` its world units, which is why both are at the root rather than under a folder. `Components/` is absent because that game has no standalone component file yet. That is the rule holding, not an omission.

## Logic and shell

The convention above describes the logic assembly; anything it may not reference — the runtime, the backend, file IO, ambient clocks, ambient randomness, asynchronous execution — lives in the shell, which otherwise holds only the generated entry point. The diagnostics are in [`architecture.md`](architecture.md#logic-boundary); the project wiring and the surrounding repository shape are in [`consuming-capsule.md`](consuming-capsule.md).
