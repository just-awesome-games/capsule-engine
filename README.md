<p align="center">
  <img src="docs/assets/capsule-hero.png" alt="Capsule — a hero stepping out of a glowing capsule as a game world materializes around it" width="720">
</p>

<h1 align="center">Capsule Engine</h1>

<p align="center">A code-first C# game engine — the whole game in one capsule, the machinery sealed inside.</p>

---

Capsule is JAG Studios' open-source engine: 2D, deterministic, code-first. It owns the frame —
loop, clock, window, input, the sim/render seam — and the world inside it: a scene, the entities on
it, and the order they update and draw in. **No editor, no serialized scene format, no project
wizard: scenes are C#, maps are data.**

Gameplay is pure by construction: a scene advances one fixed step at a time, reads input as named
actions, never touches a graphics device, and so is assertable headlessly. MonoGame is an
implementation detail — `Capsule.Runtime` marks its compile assets private, so a
`Microsoft.Xna.Framework` using in a consuming game does not compile.

## Quickstart

Install the .NET SDK selected by [`global.json`](global.json). Nothing else — no editor, no engine
SDK, no MonoGame install: a game restores the `JAG.Capsule.*` packages it pins from NuGet.org.
[`docs/consuming-capsule.md`](docs/consuming-capsule.md) has the two-project bootstrap.

A game is a shell, its scenes and its entities. The shell:

```csharp
using System.Numerics;
using Capsule.Runtime.Generated;

GameBoot.Configure("My Game")
    .WithCameraViewport(new Vector2(320f, 180f))
    .WithBindings(MyGameInput.Bind)
    .RunScene("room-01");
```

A scene in the logic project, composed from the map its name derives:

```csharp
using Capsule;
using Capsule.Scenes;

public sealed class Room01(MapSceneContext context) : MapScene(context)
{
    protected override void OnStep(in StepContext step)
    {
        if (step.Input.WasPressed(MyGameInput.Quit))
        {
            RequestExit();
        }
    }

    // Every entity has moved by now, so this frames the step that just settled.
    protected override void OnLateStep(in StepContext step) =>
        Camera.Center = FindSingle<Player>().Position;
}
```

And an entity, claiming the map objects typed `player`:

```csharp
using System.Numerics;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Spawning;

public sealed class Player : Entity
{
    public Player(EntitySpawn spawn)
        : base(spawn.Position) =>
        Add(new QuadRenderer(new Vector2(8f, 8f), new ColorRgba(0xE0, 0x6C, 0x2A)));

    public override void Update(in StepContext step) =>
        Position += new Vector2(step.Input.Axis(MyGameInput.Move) * 120f * (float)step.DeltaSeconds, 0f);
}
```

Nothing above is registered by hand and nothing is reflected for: `GameBoot` is generated into the
shell already holding the registries the compiler built from those classes. Every verb is
documented where it is defined — the XML doc comments are the API reference, in your editor and in
the packages.

## Architecture

Dependencies point one way and the build fails on a violation: Core references nothing, Maps
references Core, Scenes references Core and Maps, Runtime references all three. A game's logic
references only the substrate-free modules and its one-file shell alone references
`Capsule.Runtime` — which is why gameplay stays headless-testable, and why no game links a line of
Tiled-parsing code.

| Project | What it is |
| --- | --- |
| `Capsule.Core` | The contracts a game codes against: `ISimulation`, the fixed step, input as named actions, render intent. |
| `Capsule.Maps` | The map format and its loader ([README](Capsule.Maps/README.md)). |
| `Capsule.Maps.Cli` | The dev-time tool a build hook runs to import Tiled maps. |
| `Capsule.Scenes` | The world: `Scene`, `MapScene`, `Entity`, `Component`, `Renderer`, `Camera`. |
| `Capsule` | The pack root publishing the substrate-free modules as `JAG.Capsule`. Holds no code. |
| `Capsule.Scenes.Generator` | Turns a game's classes into the registries it boots through. Generated code, never reflection. |
| `Capsule.Analyzers` | Compile-time enforcement of logic purity, deterministic services and role legality. |
| `Capsule.Build` | The tooling-only package carrying build hooks, generators, analyzers and the map importer to a game. |
| `Capsule.Runtime` | The host: window, device, clock, input, renderer, crash log. The only project referencing MonoGame. |
| `Capsule.Tests` | xUnit specs over all of it. |

A release is three packages carrying one version — `JAG.Capsule` (every substrate-free module),
`JAG.Capsule.Runtime` (the host) and `JAG.Capsule.Build` (tooling). The package boundary is the
purity boundary; assemblies and namespaces stay `Capsule.*`.

## Further reading

- [`CONTRIBUTING.md`](CONTRIBUTING.md) — building the engine, and the gates a change passes.
- [`AGENTS.md`](AGENTS.md) — the rules a contributor works under here.
- [`docs/consuming-capsule.md`](docs/consuming-capsule.md) — what a game repository sets up.
- [`docs/architecture.md`](docs/architecture.md) — the determinism contract.
- [`Capsule.Maps/README.md`](Capsule.Maps/README.md) — the map format and the Tiled import.

Capsule is licensed under the [MIT License](LICENSE).
