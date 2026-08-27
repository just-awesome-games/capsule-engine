<p align="center">
  <img src="https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/docs/assets/capsule-hero.png" alt="Capsule — a hero stepping out of a glowing capsule as a game world materializes around it" width="720">
</p>

# Capsule Engine

A code-first C# game engine — the whole game in one capsule, the machinery sealed inside.

Capsule is JAG Studios' open-source engine: 2D, deterministic, code-first. It owns the frame —
loop, clock, window, input, the sim/render seam — and the world inside it: a scene, the entities on
it, and the order they update and draw in. **No editor, no serialized scene format, no project
wizard: scenes are C#, maps are data.**

Gameplay is pure by construction: a scene advances one fixed step at a time, reads input as named
actions, never touches a graphics device, and so is assertable headlessly. MonoGame is an
implementation detail Capsule hides — a `Microsoft.Xna.Framework` using in a consuming game does
not compile.

```csharp
using System.Numerics;
using Capsule.Runtime.Generated;
using MyGame.Game;

GameBoot.Configure("My Game")
    .WithCameraViewport(new Vector2(320f, 180f))
    .WithBindings(MyGameInput.Bind)
    .RunScene("room-01");
```

## The packages

| Package | What it is for |
| --- | --- |
| `JAG.Capsule` | Everything a game's logic is written against: the simulation seam and its fixed step, input as named actions, render intent, the map format and its loader, and the world of scenes, entities, components and the camera. Substrate-free throughout. |
| `JAG.Capsule.Runtime` | The host: window, graphics device, clock, keyboard and gamepad, renderer, crash log. |
| `JAG.Capsule.Build` | Build hooks, source generators, analyzers and the map importer. Tooling only; none of it ships in the executable. |

The package boundary is the purity boundary. `JAG.Capsule` is the set of modules that touch no
substrate, so a game references it from its logic, `JAG.Capsule.Runtime` from its one-file shell,
and `JAG.Capsule.Build` from every project.

## Setting a game up

[Consuming Capsule](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/consuming-capsule.md)
carries the bootstrap end to end: package sources, the project skeleton, role declarations, locked
restores, and the purity rule game logic is held to.

- [Repository and README](https://github.com/just-awesome-games/capsule-engine)
- [Architecture and the determinism contract](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/architecture.md)
- [The map format](https://github.com/just-awesome-games/capsule-engine/blob/main/Capsule.Maps/README.md)
- [Contributing](https://github.com/just-awesome-games/capsule-engine/blob/main/CONTRIBUTING.md)

Capsule is licensed under the
[MIT License](https://github.com/just-awesome-games/capsule-engine/blob/main/LICENSE).
