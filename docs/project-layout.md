# Project layout

Capsule suggests one directory convention for a game's logic assembly. The folder set is Capsule's own vocabulary and nothing else:

`Scenes/`, `Entities/`, `Components/`, `Cameras/`, `UI/`, `Drivers/`

That is the whole list of code folders. `Assets/` sits beside them and holds no code: it is the game's authoring tree, described in [`consuming-capsule.md`](consuming-capsule.md#repository-shape).

- A concept gets its folder as soon as it has one file. A concept with no files gets no folder.
- Nest inside a concept folder only when it becomes genuinely crowded, and nest by game domain — `Entities/Enemies/` — never one folder per class: a Capsule entity is code alone, its art under `Assets/Textures/` and its animation under `Assets/Sprites/`.
- Folders map to namespaces: `Entities/Player.cs` declares `MyGame.Game.Entities`.
- `UI/` holds the interface: the screen entities, and the menus and displays that compose them. Nothing there is spawned by a document, so its namespace claims no key.
- `Drivers/` holds the game's input drivers and carries a `.capsuleignore`, so it is built and never published: [`headless-play.md`](headless-play.md), [`consuming-capsule.md`](consuming-capsule.md#development-only-directories).
- That namespace is also the registry key the type claims, so where an entity or a scene is filed is what a document names it by; the rule is in [`scenes.md` § Entries and composition](scenes.md#entries-and-composition).

The assembly root holds the game's declarations — its collision layer names, input actions, world units and similar. Root is what the game *is*; folders are what it *contains*.

## Worked example

See [`samples/MinimalGame/`](../samples/MinimalGame/) for convention suggested by Capsule.

## Logic and shell

The convention above describes the logic assembly; anything it may not reference — the runtime, the backend, file IO, ambient clocks, ambient randomness, asynchronous execution — lives in the shell, which otherwise holds only the generated entry point. The diagnostics are in [`architecture.md`](architecture.md#logic-boundary); the project wiring and the surrounding repository shape are in [`consuming-capsule.md`](consuming-capsule.md).
