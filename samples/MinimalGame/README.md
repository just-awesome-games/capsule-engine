# MinimalGame

A complete Capsule game and the engine's consumer proof. It teaches the shapes a game is made of: a class-only scene with no document behind it, a Tiled-authored room with a class on top of it, a Capsule-native scene document claimed by no class at all, a player that walks and jumps against tile collision, and the two independent collision filters — what stops a body, and what a collider merely reports.

The layout is the one prescribed in [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md) § Repository shape: logic and shell projects under `src/`, asset sources under `src/asset-sources/`, configuration in shared `Directory.Build.*` files.

## Files

| File | What it shows |
| --- | --- |
| `src/MinimalGame.Game/MainMenu.cs` | A class-only scene: a public parameterless constructor, backed by no document, built as it is. The boot scene. |
| `src/MinimalGame.Game/Room.cs` | A scene that is a document and a class: `[SceneDocument("room")]` names the document, and the `SceneContent` constructor is the claim. Camera follow and quit live here. |
| `src/MinimalGame.Game/Player.cs` | A spawnable entity, claimed by kebab-cased class name. Walks, falls and jumps through a `KinematicBody2D`; blocks on `solid` and `platform` while detecting `sensor`. |
| `src/MinimalGame.Game/Sensor.cs` | An entity that collides without blocking: a collider on the `sensor` layer and nothing else. |
| `src/MinimalGame.Game/GameInput.cs` | The actions the game has, and the one place keys and pad buttons are named. |
| `src/MinimalGame.Shell/Program.cs` | The shell: window title, camera viewport, bindings, and the scene to boot into. Its `CapsuleBoot` entry point is generated. |
| `src/asset-sources/scenes/room.tmj` + `tiles.tsj` | The Tiled room. The tileset's tile properties carry the collision layer each tile type is on and which of its faces collide — the ledges declare `top` alone, which makes them one-way platforms. |
| `src/asset-sources/scenes/hall.scene.json` | The Capsule-native scene document, hand-authored and claimed by no class: it loads by name and plays as a plain `Scene`. The format is read strictly and admits no comment or description field, so a native document explains itself only through this table. |
| `src/asset-sources/textures/` | Texture sources. Each becomes a `GameAssets.Textures.<Name>` handle and ships to `assets/textures/`. |
| `src/asset-sources/audio/` | Audio sources, named and shipped the same way. |

## Controls

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Move | `A` / `D`, `Left` / `Right` | D-pad, left stick |
| Jump | `Space` | A |
| Confirm | `Enter`, `Space` | A |
| Quit | `Escape` | Start |

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
