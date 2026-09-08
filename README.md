<p align="center">
  <img src="docs/assets/capsule-hero.png" alt="Capsule — a hero stepping out of a glowing capsule as a game world materializes around it" width="720">
</p>

<h1 align="center">Capsule Engine</h1>

<p align="center">A deterministic, code-first 2D game engine for C#.</p>

Capsule owns the game loop, fixed-step clock, input, rendering, scenes, entities, and build pipeline. Games are authored in C# and in plain scene documents; there is no editor, project wizard, or serialized scene graph. MonoGame is an internal host dependency and is unavailable to game logic.

Capsule is a good fit for a 2D game that values headless-testable gameplay, explicit code, deterministic stepping, and a small engine surface. It is not a fit for teams that need an integrated editor, 3D, a large plugin ecosystem, or a stable 1.0 API.

## Quick start

Install the .NET SDK selected by [`global.json`](global.json), then run the sample game:

```text
git clone https://github.com/just-awesome-games/capsule-engine.git
cd capsule-engine
git config core.hooksPath .githooks
dotnet restore --locked-mode
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
```

The shell's entry point is handwritten against the generated `CapsuleBoot` builder:

```csharp
using Capsule.Runtime.Generated;
using MyGame.Game;

return CapsuleBoot.Configure("My Game").WithCommandLine(args).RunScene<MainMenu>();
```

```csharp
using Capsule.Scenes;

namespace MyGame.Game;

public sealed class MainMenu : Scene;
```

[`docs/consuming-capsule.md`](docs/consuming-capsule.md) contains a ready-to-copy repository layout and the minimal project wiring needed to start a game; [`docs/project-layout.md`](docs/project-layout.md) is the directory convention for the game's own source inside it.

## Model

Three packages ship: `JAG.Capsule` (logic API), `JAG.Capsule.Runtime` (shell host), and `JAG.Capsule.Build` (build tooling); see [`PACKAGE.md`](PACKAGE.md) for their contents.

Logic projects cannot reference the runtime, backend, file IO, ambient clocks, ambient randomness, or asynchronous execution; the randomness they may reach is the seeded `RandomSource` their scene holds. Capsule's analyzer enforces that boundary. Source generators discover scenes, spawnable entities and named assets at compile time, so games maintain no registration table and use no reflection for boot. Capsule games publish under NativeAOT, and every engine seam stays AOT-analysable.

Simulation advances on a fixed step from input snapshots. Rendering consumes the latest settled state and interpolates independently. The complete determinism guarantee is in [`docs/architecture.md`](docs/architecture.md).

That snapshot sequence comes from an input driver — a class the build discovers and the command line names — so a run is played with no window and no keyboard, and it reads the scene it is playing. `WithCommandLine(args)` gives a game the standard flags that drive it, and a scene that wants a screenshot raises the intent for the host to fulfil rather than writing a file itself; see [`docs/headless-play.md`](docs/headless-play.md).

A scene is a document, a class, or both; see [`docs/scenes.md`](docs/scenes.md) for the authoring model.

Sprite animation is authored as a sheet document and compiled into the game as typed frames and clips, played on the fixed step; see [`docs/sprite-animation.md`](docs/sprite-animation.md).

Game logic says things out loud through `Capsule.Diagnostics.Log`; the host installs a console sink at boot, and [`docs/consuming-capsule.md`](docs/consuming-capsule.md) says where the lines appear.

Public APIs are documented in their XML comments and ship beside the assemblies for editor IntelliSense. Start with `Scene`, `Entity` and `Component` for a world; `SceneSimulation` and `StepContext` for headless stepping; `InputConfiguration` and `DeviceSnapshot` for input; `CollisionWorld2D` and `Collider2D` for collision; `SpriteRenderer`, `SpriteAnimator` and `Camera` for presentation; and `CapsuleEngine` and `EngineBuilder` for the shell. [`docs/consuming-capsule.md`](docs/consuming-capsule.md#the-api-reference) locates the XML in package and source modes.

Rendering submits sprites in order. The host preloads the media a composed scene and its contents identify, caches any other texture on first rendered use for that scene, and releases scene-owned resources at transition or exit. Headless simulation loads no media.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the build and test gates, [`SECURITY.md`](SECURITY.md) for private vulnerability reporting, and [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) for community expectations.

Capsule is licensed under the [MIT License](LICENSE).
