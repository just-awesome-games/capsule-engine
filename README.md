<p align="center">
  <img src="docs/assets/capsule-hero.png" alt="Capsule — a hero stepping out of a glowing capsule as a game world materializes around it" width="720">
</p>

<h1 align="center">Capsule Engine</h1>

<p align="center">A deterministic, code-first 2D game engine for C#.</p>

Capsule owns the game loop, fixed-step clock, input, rendering, scenes, entities, maps, and build pipeline. Games author scenes in C# and maps as data; there is no editor, project wizard, or serialized scene graph. MonoGame is an internal host dependency and is unavailable to game logic.

Capsule is a good fit for a 2D game that values headless-testable gameplay, explicit code, deterministic stepping, and a small engine surface. It is not a fit for teams that need visual scene authoring, 3D, a large plugin ecosystem, or a stable 1.0 API.

## Quick start

Install the .NET SDK selected by [`global.json`](global.json), then run the repository's package-consumer game:

```text
git clone https://github.com/just-awesome-games/capsule-engine.git
cd capsule-engine
dotnet restore --locked-mode
dotnet run --project tests/PackageConsumer/Shell -p:CapsuleSourcePath=.
```

A Capsule game has a substrate-free logic project and a small executable shell. The generated boot API starts a scene:

```csharp
using Capsule.Runtime.Generated;
using MyGame.Game;

GameBoot.Configure("My Game").RunScene<MainMenu>();
```

```csharp
using Capsule.Scenes;

namespace MyGame.Game;

public sealed class MainMenu : Scene;
```

[`docs/consuming-capsule.md`](docs/consuming-capsule.md) contains the minimal project wiring needed to start a game.

## Model

- `JAG.Capsule` is the substrate-free API used by game logic: fixed-step contracts, input actions, render intents, maps, scenes, entities, components, and cameras.
- `JAG.Capsule.Runtime` is the executable host. Only the shell references it.
- `JAG.Capsule.Build` supplies analyzers, source generators, asset hooks, and map import. It does not ship in the game.

Logic projects cannot reference the runtime, backend, file IO, ambient clocks, randomness, or asynchronous execution. Capsule's analyzer enforces that boundary. Source generators discover scenes, spawnable entities, and shipped assets at compile time; games maintain no registration table and use no reflection for boot.

Simulation advances on a fixed step from input snapshots. Rendering consumes the latest settled state and interpolates independently. The complete determinism guarantee is in [`docs/architecture.md`](docs/architecture.md).

Maps are canonical JSON derived during the build from native map sources or Tiled files. Their format and importer constraints are in [`src/Capsule.Maps/README.md`](src/Capsule.Maps/README.md).

Public APIs are documented in their XML comments and ship beside the assemblies for editor IntelliSense.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the build and test gates, [`SECURITY.md`](SECURITY.md) for private vulnerability reporting, and [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) for community expectations.

Capsule is licensed under the [MIT License](LICENSE).
