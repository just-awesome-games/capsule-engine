<p align="center">
  <img src="https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/docs/assets/capsule-hero.png" alt="Capsule — a hero stepping out of a glowing capsule as a game world materializes around it" width="720">
</p>

# Capsule Engine

A code-first C# game engine — the whole game in one capsule, the machinery sealed inside.

Capsule is JAG Studios' open-source engine: 2D, deterministic, code-first. It owns the frame —
loop, clock, window, input, the sim/render seam, the determinism contract — and the world inside
it: a scene, the entities on it, and the order they update and draw in. A game brings its own
`Program.cs`, its scenes, and its entities. **No editor, no serialized scene format, no project
wizard: scenes are C#, maps are data.**

Gameplay is pure by construction. A scene advances one fixed step at a time, reads input as named
actions, never touches a graphics device, and so is assertable headlessly. MonoGame is an
implementation detail Capsule hides: a `Microsoft.Xna.Framework` using in a consuming game does
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
| `JAG.Capsule.Core` | The pure contracts a game codes against: the simulation seam, the fixed step, input as named actions, render intent. |
| `JAG.Capsule.Maps` | The map format and its loader: a tile grid, what its tiles draw as, and the typed objects placed over it. |
| `JAG.Capsule.Scenes` | The world a game composes: scenes, entities, components, renderers, the camera, and the step choreography. |
| `JAG.Capsule.Runtime` | The host: window, graphics device, clock, keyboard and gamepad, renderer, crash log. |
| `JAG.Capsule.Build` | Build hooks, source generators, analyzers and the map importer. Tooling only; none of it ships in the executable. |

A game references `JAG.Capsule.Build` from every project, `Core`/`Maps`/`Scenes` from its logic,
and `Runtime` from its one-file shell.

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
