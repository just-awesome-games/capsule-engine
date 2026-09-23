# MinimalGame

A complete Capsule game and the engine's consumer proof, in the repository shape and layout
[`docs/build-and-publish.md`](../../docs/build-and-publish.md) describes. It shows each shape a game is made
of, once:

- A class-only scene (`Scenes/MainMenu.cs`), and documents naming a base and a camera (`Assets/Scenes/room.scene.json`, `Assets/Scenes/halls/hall.scene.json`), with the shared base `Scenes/PlayableScene.cs` between play and level. The room document also authors its dusk `ambient`.
- A `Camera` subclass (`Cameras/GameCamera.cs`) that finds its subject and follows it, and a parallax background under the room: a screen-fixed layer whose factor and tiling are its own (`Entities/Sky.cs`) and a distant one whose `scrollFactor` the document authors (`Entities/Hills.cs`). Every texture packs onto one atlas page through `Assets/Atlases/game.atlas.json` with no call site knowing.
- A spawnable entity (`Entities/Player.cs`) walking and jumping through a `KinematicBody2D` over two colliders, one the sweep stops on and one that only reports, animated from the sheet `Assets/Sprites/actors/player.sheet.json` and firing a bolt (`Entities/Bolt.cs`) from that sheet's `muzzle` socket at the pointer through `Camera.CanvasToWorld`, with its designer-owned levers in `Entities/PlayerTuning.cs` and `Entities/BoltTuning.cs`. An entity that collides without blocking (`Entities/Hazard.cs`).
- A lift the player rides and is crushed under (`Entities/Lift.cs`), a platform circling over the ledges and a block sliding along the floor (`Entities/CirclingPlatform.cs`, `Entities/Shuttle.cs`), and a brick over the spawn that a head-bump clears from the room's tile map at run time (`Entities/Player.cs`).
- A screen-space interface: a title menu, an options screen and a pause menu a pointer or a gamepad drives (`UI/TitleMenu.cs`, `UI/OptionsMenu.cs`, `UI/PauseMenu.cs`, `UI/MenuItem.cs`) and a head-up display bound to simulation state (`UI/PlayerHud.cs`, `UI/HealthBar.cs`), on a font from `Assets/Fonts/`.
- Run-owned music (`Assets/Audio/Music/title.ogg`, `room.ogg`) crossfaded between the menu and the room and faded out on death, and a bus fade under the Options sound toggle. The music loops are synthesized in-house, not third-party.
- Three `ParticleEmitter` patterns: a burst on landing (`Entities/Player.cs`), a continuous trail on a moving entity (`Entities/Hazard.cs`), and a burst as its own entity, outliving the bolt that spawns it and removing itself once its last particle dies (`Entities/SparkBurst.cs`, `Entities/Bolt.cs`).
- One save document (`GameSaves.cs`): the options screen's Sound item and Jump/Shoot rebinding write it, and `GameBoot.Start` reads it back through `WithRunStart` when the run starts.
- The game's declarations at the assembly root (`GameInput.cs`, `GameBoot.cs`, `GameInstance.cs`, `RunExtensions.cs`, `Music.cs`, `AudioBuses.cs`, `CollisionLayers.cs`, `TileTypes.cs`, `GameSaves.cs`, `World.cs`), the shell (`src/MinimalGame.Shell/Program.cs`), an input driver behind `Drivers/.capsuleignore`, and tests at both boundaries [`docs/testing.md`](../../docs/testing.md) names (`tests/MinimalGame.Tests/`).

`src/MinimalGame.Shell/ConsumerProof.targets` is CI's assertion over the shipped package, not game wiring: a
copy of the sample drops the import and replaces the tests.

## Running

From the engine repository root, where the sample builds from engine source by default:

```sh
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --scene room --driver Walkthrough --headless
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --scene halls/hall --driver Walkthrough --headless
dotnet test samples/MinimalGame/MinimalGame.slnx
```

Against the NuGet packages instead:

```sh
dotnet pack --configuration Release --output artifacts/packages
dotnet restore samples/MinimalGame/MinimalGame.slnx --configfile samples/MinimalGame/NuGet.config -p:CapsuleUsePackages=true
dotnet build samples/MinimalGame/MinimalGame.slnx --configuration Release --no-restore -p:CapsuleUsePackages=true
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell --configuration Release --no-restore -p:CapsuleUsePackages=true
```

The gate is `sh hooks/pre-commit` from the sample root. A copy of the sample makes it the commit hook with
`git config core.hooksPath hooks`. Inside the engine repository the engine's own `.githooks` stays the
hook.

The controls are `GameInput.cs`: move with A/D, the arrows, the d-pad or the left stick, jump and shoot with
whatever the title menu's Options screen has them bound to (Space and the south pad button, the left mouse
button and the west pad button, by default), and pause with Escape or Start, where the pause menu
resumes or quits. The game reports jumps, shots and hazard contacts through `Capsule.Diagnostics.Log`
on the console it was launched from.
