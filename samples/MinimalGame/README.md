# MinimalGame

A complete Capsule game and the engine's consumer proof. It teaches the shapes a game is made of: a class-only scene with no document behind it, a Tiled-authored room with a class on top of it, a Capsule-native scene document claimed by no class at all, a player that walks and jumps against tile collision, and the two independent collision filters — what stops a body, and what a collider merely reports.

The repository shape is the one prescribed in [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md) § Repository shape: logic and shell projects under `src/`, asset sources under `src/asset-sources/`, configuration in shared `Directory.Build.*` files. Inside the logic project the folders follow [`docs/project-layout.md`](../../docs/project-layout.md).

## Files

| File                                              | What it shows                                                                                                                                                                                                                                                                               |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/MinimalGame.Game/Scenes/MainMenu.cs`         | A class-only scene: a public parameterless constructor, backed by no document, built as it is. The boot scene.                                                                                                                                                                              |
| `src/MinimalGame.Game/Scenes/Room.cs`             | A scene that is a document and a class: `[SceneDocument("room")]` names the document, and the `SceneContent` constructor is the claim. It installs the camera and handles quitting.                                                                                                         |
| `src/MinimalGame.Game/Cameras/GameCamera.cs`      | A `Camera` subclass: the game's viewport span, the subject it finds for itself in `OnStart`, and the follow it settles in `OnLateStep`. Scenes install it and touch it no further.                                                                                                          |
| `src/MinimalGame.Game/Entities/Player.cs`         | A spawnable entity, claimed by kebab-cased class name. Draws a `Sprite` over the whole of `player.png` and faces its walk direction with `FlipX`; walks, falls and jumps through a `KinematicBody2D`; blocks on `solid` and `platform` while detecting `sensor`. Its frame is anchored bottom-centre so `Scale` squashes and stretches it about the feet on take-off and landing, and the collider never follows. |
| `src/MinimalGame.Game/Entities/Sensor.cs`         | An entity that collides without blocking: a translucent sprite, a collider on the `sensor` layer, and nothing else.                                                                                                                                                                         |
| `src/MinimalGame.Game/GameInput.cs`               | The actions the game has, and the one place keys and pad buttons are named. At the assembly root because it is a declaration, not a content of the game.                                                                                                                                    |
| `src/MinimalGame.Game/World.cs`                   | The game's world units, declared once at the root and read by every camera that spans them.                                                                                                                                                                                                 |
| `src/MinimalGame.Shell/Program.cs`                | The shell: window title, bindings, point sampling for the pixel art, and the scene to boot into. Its `CapsuleBoot` entry point is generated.                                                                                                                                                |
| `src/asset-sources/scenes/room.tmj` + `tiles.tsj` | The Tiled room. `tiles.tsj` is an image tileset over `textures/tiles.png`, so each tile draws the cell it occupies; its tile properties carry the collision layer each tile type is on and which of its faces collide — the ledges declare `top` alone, which makes them one-way platforms. |
| `src/asset-sources/scenes/hall.scene.json`        | The Capsule-native scene document, hand-authored and claimed by no class: it loads by name and plays as a plain `Scene`. The format is read strictly and admits no comment or description field, so a native document explains itself only through this table.                              |
| `src/asset-sources/textures/`                     | Texture sources: the player's frame, the sensor's field, and the terrain atlas the tileset cuts. Each becomes a `GameAssets.Textures.<Name>` handle, ships to `assets/textures/`, and is resident from boot.                                                                                |
| `src/asset-sources/audio/`                        | Audio sources, named and shipped the same way.                                                                                                                                                                                                                                              |

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
