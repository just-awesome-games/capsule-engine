# MinimalGame

A complete Capsule game and the engine's consumer proof. It teaches the shapes a game is made of: a class-only scene with no document behind it, a hand-authored room document with a class on top of it, a Capsule-native scene document claimed by no class at all, a player that walks and jumps against tile collision, an animated sprite sheet played on the fixed step, a screen-space interface — a menu a pointer or a gamepad drives, and a head-up display over the room — and the two independent collision filters, what stops a body and what a collider merely reports.

The repository shape is the one prescribed in [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md) § Repository shape: logic and shell projects under `src/`, the authoring tree under `src/MinimalGame.Game/Assets/`, configuration in shared `Directory.Build.*` files. Inside the logic project the folders follow [`docs/project-layout.md`](../../docs/project-layout.md).

## Files

| File                                              | What it shows                                                                                                                                                                                                                                                                               |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/MinimalGame.Game/Scenes/MainMenu.cs`         | A class-only scene: a public parameterless constructor, backed by no document, built as it is. The boot scene. Everything it adds is a `ScreenEntity` anchored to the canvas, with a `FocusNavigator<Label>` raising the events the scene shows the focus from. |
| `src/MinimalGame.Game/Scenes/Room.cs`             | A scene that is a document and a class: `[SceneDocument("room")]` names the document, and the `SceneContent` constructor is the claim. It installs the camera, adds the health bar over the document's contents, returns to the menu at no health, and handles quitting.                                                                                                         |
| `src/MinimalGame.Game/Cameras/GameCamera.cs`      | A `Camera` subclass: the game's viewport span, the subject it finds for itself in `OnStart`, and the follow it settles in `OnLateStep`. Scenes install it and touch it no further.                                                                                                          |
| `src/MinimalGame.Game/Entities/Player.cs`         | A spawnable entity, claiming the key its namespace names: it sits directly under `Entities`, so it answers to `player`. A `SpriteAnimator` plays the sheet's `idle` and `walk` clips into its `SpriteRenderer`, which faces the walk direction with `FlipX`; it walks, falls and jumps through a `KinematicBody2D` over two colliders: an 8x8 body the sweep stops, blocking on `solid` and `platform`, and a smaller hurtbox inset inside it that reports `sensor` contacts through named handlers and spends one of its four health points per contact entered. Its frames are anchored bottom-centre so `Scale` squashes and stretches them about the feet on take-off and landing, and the collider never follows. |
| `src/MinimalGame.Game/Entities/PlayerTuning.cs`   | The player's designer-owned levers as one `readonly record struct` with a `Default`, each parameter documented with its unit and which way to move it. This is the pattern that stands in for a Unity ScriptableObject or a Godot Resource; nothing in the engine knows it exists. |
| `src/MinimalGame.Game/Entities/Sensor.cs`         | An entity that collides without blocking: a translucent sprite, a collider on the `sensor` layer, and nothing else.                                                                                                                                                                         |
| `src/MinimalGame.Game/GameInput.cs`               | The actions the game has, the `FocusActions` a menu is navigated by, and the one place keys, pad and mouse buttons are named. At the assembly root because it is a declaration, not a content of the game.                                                                                                                                    |
| `src/MinimalGame.Game/World.cs`                   | The game's world units, declared once at the root and read by every camera that spans them.                                                                                                                                                                                                 |
| `src/MinimalGame.Shell/Program.cs`                | The shell: window title, bindings, the 320x180 render resolution that is also the canvas, point sampling for the pixel art, and the scene to boot into. Its `CapsuleBoot` entry point is generated.                                                                                                                                                |
| `src/MinimalGame.Game/Assets/Scenes/room.scene.json` | The room, authored by hand in Capsule's own format. Its `tile-map` entry draws from `textures/tiles.png`, so each tile draws the cell it occupies; its palette carries the collision layer each tile type is on and which of its faces collide — the ledges declare `top` alone, which makes them one-way platforms. |
| `src/MinimalGame.Game/Assets/Scenes/halls/hall.scene.json`  | The Capsule-native scene document, hand-authored and claimed by no class: it is keyed `halls/hall` by the directory it sits in, ships at `assets/scenes/halls/hall.scene.json`, loads by that key and plays as a plain `Scene`. The format is read strictly and admits no comment or description field, so a native document explains itself only through this table.                              |
| `src/MinimalGame.Game/Assets/Sprites/actors/player.sheet.json` | The player's sheet: the six frames it cuts from `textures/actors/player.png`, each with its bottom-centre pivot, and the looping `idle` and `walk` clips over them in ticks. Its directory is part of its key, so the build compiles it into `CapsuleAssets.Sprites.Actors.Player`; nothing of it ships.                                                  |
| `src/MinimalGame.Game/Assets/Textures/`                     | Texture sources: the player's frame strip under `actors/`, the sensor's field and the terrain atlas at the root. A source's path under the domain root is its handle and its class path, so the strip is `CapsuleAssets.Textures.Actors.Player` and ships to `assets/textures/actors/player.png`. All three are resident from boot.                                                                          |
| `src/MinimalGame.Game/Assets/Audio/`                        | Audio sources, named and shipped the same way.                                                                                                                                                                                                                                              |
| `src/MinimalGame.Game/Assets/Fonts/`                        | The menu font: a BMFont description and the page it was baked onto. `menu.fnt` compiles into `CapsuleAssets.Fonts.Menu` and ships nothing; `menu.png` ships to `assets/fonts/menu.png`. See [`docs/text.md`](../../docs/text.md).                                                            |
| `THIRD-PARTY-NOTICES.md`                          | The license the font's pages are distributed under.                                                                                                                                                                                                                                         |
| `src/MinimalGame.Shell/ConsumerProof.targets`     | CI's assertions over the shipped package — that the backend never reaches a game's compile references, and that the packed layout embeds the window icon. Not game wiring: a copy of this sample drops the import.                                                                           |

## Controls

| Action    | Keyboard                    | Gamepad                     | Mouse       |
| --------- | --------------------------- | --------------------------- | ----------- |
| Move      | `A` / `D`, `Left` / `Right` | D-pad, left stick           |             |
| Jump      | `Space`                     | A                           |             |
| Menu up   | `W`, `Up`                   | D-pad up, left stick up     |             |
| Menu down | `S`, `Down`                 | D-pad down, left stick down |             |
| Confirm   | `Enter`, `Space`            | A                           |             |
| Click     |                             |                             | Left button |
| Quit      | `Escape`                    | Start                       |             |

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

The menu opens focused on Start: moving the focus picks the other item, and confirming or clicking one enters the room or leaves. The room draws the player's health over the world, and returns to the menu once four sensor contacts have spent it. The game talks through `Capsule.Diagnostics.Log` as well, and the shell installs a console sink at boot: jumps, landings and sensor contacts appear on the console the game was launched from, each line prefixed with the tick it happened on.
