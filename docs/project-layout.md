# Project layout

One directory convention for a game's logic assembly, in Capsule's own vocabulary: `Scenes/`, `Entities/`, `Components/`, `Cameras/`, `UI/`, `Drivers/`, with `Assets/` — the authoring tree of [`consuming-capsule.md`](consuming-capsule.md#repository-shape) — beside them and holding no code.

- A concept gets its folder as soon as it has one file, and no folder before.
- Nest inside a concept folder only when it is genuinely crowded, and by game domain — `Entities/Enemies/` — never one folder per class: an entity is code alone, its art under `Assets/Textures/`, its animation under `Assets/Sprites/`.
- Folders map to namespaces (`Entities/Player.cs` declares `MyGame.Game.Entities`), and the namespace is the registry key a scene or entity claims, so where a type is filed is what a document names it by ([`scenes.md` § Entries and composition](scenes.md#entries-and-composition)).
- `UI/` holds the screen entities and the menus and displays that compose them; nothing there is spawned by a document.
- `Drivers/` holds the input drivers behind a `.capsuleignore`: built always, published never ([`headless-play.md`](headless-play.md)).
- The assembly root holds the game's declarations — collision layer names, input actions, world units: root is what the game *is*, folders are what it *contains*.

Anything the logic assembly may not reference — the runtime, the backend, file I/O, ambient clocks, ambient randomness, asynchronous execution ([`architecture.md`](architecture.md#logic-boundary)) — lives in the shell, which otherwise holds only its generated entry point. [`samples/MinimalGame/`](../samples/MinimalGame/) is the worked example.
