# MinimalGame

A complete Capsule game and the engine's consumer proof, in the repository shape of [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md) and the layout of [`docs/project-layout.md`](../../docs/project-layout.md). It shows each shape a game is made of, once:

- A class-only scene (`Scenes/MainMenu.cs`), a document with a class on top (`Scenes/Room.cs` over `Assets/Scenes/room.scene.json`), and a document claimed by no class (`Assets/Scenes/halls/hall.scene.json`), with the shared base `Scenes/PlayableScene.cs` between play and level.
- A `Camera` subclass (`Cameras/GameCamera.cs`) that finds its subject and follows it.
- A spawnable entity (`Entities/Player.cs`) walking and jumping through a `KinematicBody2D` over two colliders — one the sweep stops on, one that only reports — animated from the sheet `Assets/Sprites/actors/player.sheet.json`, with its designer-owned levers in `Entities/PlayerTuning.cs`; and an entity that collides without blocking (`Entities/Hazard.cs`).
- A screen-space interface: a menu a pointer or a gamepad drives (`UI/TitleMenu.cs`, `UI/TitleMenuItem.cs`) and a head-up display bound to simulation state (`UI/PlayerHud.cs`, `UI/HealthBar.cs`), on a font from `Assets/Fonts/`.
- The game's declarations at the assembly root (`GameInput.cs`, `World.cs`), the shell (`src/MinimalGame.Shell/Program.cs`), an input driver behind `Drivers/.capsuleignore`, and tests at both boundaries [`docs/testing.md`](../../docs/testing.md) names (`tests/MinimalGame.Tests/`).

`src/MinimalGame.Shell/ConsumerProof.targets` is CI's assertion over the shipped package, not game wiring: a copy of the sample drops the import and replaces the tests.

## Running

From the engine repository root, where the sample builds from engine source by default:

```sh
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --scene Room --driver Walkthrough --headless
dotnet test samples/MinimalGame/MinimalGame.slnx
```

Against the NuGet packages instead:

```sh
dotnet pack --configuration Release --output artifacts/packages
dotnet restore samples/MinimalGame/MinimalGame.slnx --configfile samples/MinimalGame/NuGet.config -p:CapsuleUsePackages=true
dotnet build samples/MinimalGame/MinimalGame.slnx --configuration Release --no-restore -p:CapsuleUsePackages=true
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell --configuration Release --no-restore -p:CapsuleUsePackages=true
```

The gate is `sh hooks/pre-commit` from the sample root; a copy of the sample makes it the commit hook with `git config core.hooksPath hooks`, while inside the engine repository the engine's own `.githooks` stays the hook. The controls are `GameInput.cs`; the game reports jumps, landings and hazard contacts through `Capsule.Diagnostics.Log` on the console it was launched from.
