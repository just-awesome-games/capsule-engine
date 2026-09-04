# MinimalGame

A complete Capsule game and the engine's consumer proof. It teaches the shapes a game is made of: a class-only scene with no document behind it, a hand-authored room document with a class on top of it, a Capsule-native scene document claimed by no class at all, a player that walks and jumps against tile collision, an animated sprite sheet played on the fixed step, and the two independent collision filters — what stops a body, and what a collider merely reports.

The repository shape is the one prescribed in [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md) § Repository shape: logic and shell projects under `src/`, asset sources under `src/asset-sources/`, configuration in shared `Directory.Build.*` files. Inside the logic project the folders follow [`docs/project-layout.md`](../../docs/project-layout.md).

## Files

| File                                              | What it shows                                                                                                                                                                                                                                                                               |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/MinimalGame.Game/Scenes/MainMenu.cs`         | A class-only scene: a public parameterless constructor, backed by no document, built as it is. The boot scene.                                                                                                                                                                              |
| `src/MinimalGame.Game/Scenes/Room.cs`             | A scene that is a document and a class: `[SceneDocument("room")]` names the document, and the `SceneContent` constructor is the claim. It installs the camera and handles quitting.                                                                                                         |
| `src/MinimalGame.Game/Cameras/GameCamera.cs`      | A `Camera` subclass: the game's viewport span, the subject it finds for itself in `OnStart`, and the follow it settles in `OnLateStep`. Scenes install it and touch it no further.                                                                                                          |
| `src/MinimalGame.Game/Entities/Player.cs`         | A spawnable entity, claiming the key its namespace names: it sits directly under `Entities`, so it answers to `player`. A `SpriteAnimator` plays the sheet's `idle` and `walk` clips into its `SpriteRenderer`, which faces the walk direction with `FlipX`; it walks, falls and jumps through a `KinematicBody2D`, blocking on `solid` and `platform` while detecting `sensor`. Its frames are anchored bottom-centre so `Scale` squashes and stretches them about the feet on take-off and landing, and the collider never follows. |
| `src/MinimalGame.Game/Entities/Sensor.cs`         | An entity that collides without blocking: a translucent sprite, a collider on the `sensor` layer, and nothing else.                                                                                                                                                                         |
| `src/MinimalGame.Game/GameInput.cs`               | The actions the game has, and the one place keys and pad buttons are named. At the assembly root because it is a declaration, not a content of the game.                                                                                                                                    |
| `src/MinimalGame.Game/World.cs`                   | The game's world units, declared once at the root and read by every camera that spans them.                                                                                                                                                                                                 |
| `src/MinimalGame.Shell/Program.cs`                | The shell: window title, bindings, point sampling for the pixel art, and the scene to boot into. Its `CapsuleBoot` entry point is generated.                                                                                                                                                |
| `src/asset-sources/scenes/room.scene.json` | The room, authored by hand in Capsule's own format. Its `tile-map` entry draws from `textures/tiles.png`, so each tile draws the cell it occupies; its palette carries the collision layer each tile type is on and which of its faces collide — the ledges declare `top` alone, which makes them one-way platforms. |
| `src/asset-sources/scenes/halls/hall.scene.json`  | The Capsule-native scene document, hand-authored and claimed by no class: it is keyed `halls/hall` by the directory it sits in, ships at `assets/scenes/halls/hall.scene.json`, loads by that key and plays as a plain `Scene`. The format is read strictly and admits no comment or description field, so a native document explains itself only through this table.                              |
| `src/asset-sources/sprites/actors/player.sheet.json` | The player's sheet: the six frames it cuts from `textures/actors/player.png`, each with its bottom-centre pivot, and the looping `idle` and `walk` clips over them in ticks. Its directory is part of its key, so the build compiles it into `GameSprites.Actors.Player`; nothing of it ships.                                                  |
| `src/asset-sources/textures/`                     | Texture sources: the player's frame strip under `actors/`, the sensor's field and the terrain atlas at the root. A source's path under the domain root is its handle and its class path, so the strip is `GameAssets.Textures.Actors.Player` and ships to `assets/textures/actors/player.png`. All three are resident from boot.                                                                          |
| `src/asset-sources/audio/`                        | Audio sources, named and shipped the same way.                                                                                                                                                                                                                                              |
| `src/MinimalGame.Shell/ConsumerProof.targets`     | CI's assertions over the shipped package — that the backend never reaches a game's compile references, and that the packed layout embeds the window icon. Not game wiring: a copy of this sample drops the import.                                                                           |

## Controls

| Action  | Keyboard                    | Gamepad           |
| ------- | --------------------------- | ----------------- |
| Move    | `A` / `D`, `Left` / `Right` | D-pad, left stick |
| Jump    | `Space`                     | A                 |
| Confirm | `Enter`, `Space`            | A                 |
| Quit    | `Escape`                    | Start             |

## Running

From the engine repository root. Inside the repository the sample builds from engine source by default:

```sh
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
```

To run against the NuGet packages instead:

```sh
dotnet pack --configuration Release --output artifacts/packages
dotnet restore samples/MinimalGame/MinimalGame.slnx --configfile samples/MinimalGame/NuGet.config -p:CapsuleUsePackages=true
dotnet build samples/MinimalGame/MinimalGame.slnx --configuration Release --no-restore -p:CapsuleUsePackages=true
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell --configuration Release --no-restore -p:CapsuleUsePackages=true
```

The game talks through `Capsule.Diagnostics.Log`, and the shell installs a console sink at boot: the menu prompt, jumps, landings and sensor contacts all appear on the console the game was launched from, each line prefixed with the tick it happened on.
