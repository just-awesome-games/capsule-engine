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

A shell's whole hand-written code is its entry point, against the generated `CapsuleBoot` builder:

```csharp
return CapsuleBoot.Configure("My Game").WithCommandLine(args).RunScene<MainMenu>();
```

Press `` ` `` in any windowed run for the development overlay; a trimmed publish removes it and an untrimmed one carries it disabled ([`docs/debugging.md`](docs/debugging.md)).

## Where to go next

Three packages ship: `JAG.Capsule` (logic API), `JAG.Capsule.Runtime` (shell host), and `JAG.Capsule.Build` (build tooling); [`PACKAGE.md`](PACKAGE.md) lists their contents. Every public member's contract is its XML documentation, shipped beside the assemblies; the markdown below holds only what spans many types.

- [`docs/architecture.md`](docs/architecture.md) — the module boundaries, the logic boundary, the determinism contract, and the NativeAOT floor.
- [`docs/consuming-capsule.md`](docs/consuming-capsule.md) — repository shape, project wiring, publishing, and the build properties.
- [`docs/workflow.md`](docs/workflow.md) — the everyday commands of a game built on Capsule.
- [`docs/debugging.md`](docs/debugging.md) — the development plane: the overlay and what a shipping publish drops.
- [`docs/project-layout.md`](docs/project-layout.md) — the directory convention inside a game's logic project.
- [`docs/scenes.md`](docs/scenes.md) — the scene authoring model and the `*.scene.json` format.
- [`docs/sprite-animation.md`](docs/sprite-animation.md) — the `*.sheet.json` sprite sheet format.
- [`docs/text.md`](docs/text.md) — bitmap fonts.
- [`docs/headless-play.md`](docs/headless-play.md) — input drivers and the standard command line.
- [`docs/testing.md`](docs/testing.md) — which boundary to test a game at.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for setup and the gate, [`AGENTS.md`](AGENTS.md) for the rules no compiler enforces, [`SECURITY.md`](SECURITY.md) for private vulnerability reporting, and [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) for community expectations.

Capsule is licensed under the [MIT License](LICENSE).
The runtime's embedded font is covered by the [third-party notices](THIRD-PARTY-NOTICES.md).
