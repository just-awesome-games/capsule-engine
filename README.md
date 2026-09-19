<p align="center">
  <img src="docs/assets/capsule-hero.png" alt="Capsule, a hero stepping out of a glowing capsule as a game world materializes around it" width="720">
</p>

<h1 align="center">Capsule Engine</h1>

<p align="center">A deterministic, code-first 2D game engine for C#.</p>

Capsule owns the game loop, fixed-step clock, input, rendering, scenes, entities, saves, and build pipeline. Games are authored in C# and in plain scene documents. There is no editor, no project wizard and no serialized scene graph. MonoGame is an internal host dependency and is unavailable to game logic.

Capsule is a good fit for a 2D game that values headless-testable gameplay, explicit code, deterministic stepping, and a small engine surface. It is not a fit for teams that need an integrated editor, 3D, a large plugin ecosystem, or a stable 1.0 API.

## Quick start

Install the .NET SDK selected by [`global.json`](global.json), then run the sample game:

```text
git clone https://github.com/just-awesome-games/capsule-engine.git
cd capsule-engine
dotnet restore --locked-mode
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
```

[`docs/getting-started.md`](docs/getting-started.md) takes it from there: three projects, a scene, and one entity on screen. A shell's only hand-written code is its entry point, against the generated `CapsuleBoot` builder:

```csharp
return CapsuleBoot.Configure("My Game", new DesktopPlatform()).WithCommandLine(args).RunScene<MainMenu>();
```

Press `` ` `` in any windowed run for the development overlay. A trimmed publish removes it and an untrimmed one carries it disabled ([`docs/debugging.md`](docs/debugging.md)).

## Documentation

Four packages ship: `JAG.Capsule` (logic API), `JAG.Capsule.Runtime` (the neutral host), `JAG.Capsule.Runtime.Desktop` (the desktop platform module a shell references), and `JAG.Capsule.Build` (build tooling). [`PACKAGE.md`](PACKAGE.md) lists their contents. Every public member's contract is its XML documentation, shipped beside the assemblies. The pages below are the tasks that span many types.

- [`docs/getting-started.md`](docs/getting-started.md): clone, wire three projects, put one entity on screen, run it.
- [`docs/input.md`](docs/input.md): actions, axes, the pointer, input drivers, the standard command line.
- [`docs/rendering.md`](docs/rendering.md): the canvas, cameras, draw order, sprites, text, parallax.
- [`docs/audio.md`](docs/audio.md): clips, buses, sources, the mixer.
- [`docs/collision.md`](docs/collision.md): colliders, layers, contacts, kinematic movement, queries.
- [`docs/assets.md`](docs/assets.md): asset keys, textures, sprite sheets, atlases, audio, fonts, preloading.
- [`docs/scenes.md`](docs/scenes.md): the scene authoring model and the `*.scene.json` format.
- [`docs/persistence.md`](docs/persistence.md): save documents, the file format, and where they go.
- [`docs/build-and-publish.md`](docs/build-and-publish.md): project wiring, build properties, publishing, platform modules.
- [`docs/testing.md`](docs/testing.md): which boundary to test a game at.
- [`docs/debugging.md`](docs/debugging.md): the development overlay, debug draw, panels, logging.
- [`docs/architecture.md`](docs/architecture.md): module boundaries, the logic boundary, the determinism contract, the NativeAOT floor.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for setup and the gate, [`AGENTS.md`](AGENTS.md) for the rules no compiler enforces, [`SECURITY.md`](SECURITY.md) for private vulnerability reporting, and [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) for community expectations.

Capsule is licensed under the [MIT License](LICENSE).
The runtime's embedded font is covered by the [third-party notices](THIRD-PARTY-NOTICES.md).
