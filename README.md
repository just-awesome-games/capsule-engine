<p align="center">
  <img src="docs/assets/capsule-hero.png" alt="Capsule — a game world inside a capsule" width="720">
</p>

<h1 align="center">Capsule</h1>

<p align="center">A code-first C# game engine — the whole game in one capsule, the machinery sealed inside.</p>

---

Capsule is JAG Studios' in-house engine: 2D, pixel-art, deterministic, code-first. It is an
**opinionated application runtime** — it owns the frame (loop, clock, window, input, the
sim/render seam, the determinism contract) and everything above that line lands only with the
game call site that needs it. It is a library, not an application: a game brings its own
`Program.cs` and hands the host a simulation. No editor, no scene format, no project wizard.

- **Everything is C# and text on disk** — the whole surface reachable by a person and an agent alike.
- **Gameplay is pure by construction.** A simulation advances one fixed step at a time, reads input
  as named actions, never touches a graphics device, and so is assertable headlessly.
- **MonoGame is an implementation detail.** `Capsule.Runtime` marks its compile assets private, so a
  `Microsoft.Xna.Framework` using in a consuming game does not compile, while MonoGame's managed and
  native libraries still reach that game's output. Swapping the backend is engine-side only.

## Quickstart

A complete Capsule game's entry point:

```csharp
using Capsule.Input;
using Capsule.Runtime;
using MyGame.Systems;

CapsuleEngine.Configure()
    .WithWindow("My Game", 1280, 720, resizable: true)
    .WithFixedStep(60)
    .WithCrashLog("MyGame")
    .WithBindings(bindings => bindings.Bind(MyGameActions.Quit, Key.Escape))
    .Run(new MyGameSimulation());
```

`MyGameSimulation` implements [`ISimulation`](Capsule.Core/README.md#isimulation): it advances
one fixed step at a time, reads input as named actions, sets `ExitRequested` when it wants to
stop, and exposes what to draw as a `FrameView`. It never touches a graphics device.

Games reference the engine as a **sibling clone, by project reference** — no packaging, no feed,
no version dance:

```
git clone https://github.com/just-awesome-games/capsule-engine.git   # beside the game repo
```

Game logic references `Capsule.Core` only; the one-file shell references `Capsule.Runtime`.
That split is the compiler-enforced guarantee that gameplay stays pure and headless-testable.

## Repository layout

| Path                       | Contents                                                                                        |
| -------------------------- | ------------------------------------------------------------------------------------------------- |
| `Capsule.sln`              | Solution                                                                                        |
| `Capsule.Core/`            | Pure engine contracts — simulation, input, render intent. **No package references at all**      |
| `Capsule.Runtime/`         | The host: window, fixed-step loop, keyboard sampling, renderer, crash log. Owns MonoGame        |
| `Capsule.Tests/`           | xUnit specs over `Capsule.Core`, plus builder validation and the public-surface guard           |
| `docs/`                    | Cross-cutting architecture                                                                      |
| `Directory.Build.props`    | Solution-wide compiler settings: nullable, warnings-as-errors, analyzers, code style, lock files |
| `global.json`              | Pinned .NET SDK (10.0.301)                                                                      |
| `hooks/`                   | Committed git hooks                                                                             |
| `.github/workflows/ci.yml` | CI                                                                                              |

## Documentation

| Document | Read it for |
| --- | --- |
| [`AGENTS.md`](AGENTS.md) | The rules any contributor — human or agent — works under here, and the studio's binding MonoGame standard behind them (design repo: `studio/technical/engines/monogame/best-practices.md`) |
| [`Capsule.Core/README.md`](Capsule.Core/README.md) | The contracts a game codes against: simulation, input, render intent |
| [`Capsule.Runtime/README.md`](Capsule.Runtime/README.md) | The builder, the loop contract, the MonoGame-hiding contract, crash logging |
| [`docs/architecture.md`](docs/architecture.md) | Project map, dependency directions, the input and render pipelines end to end, and the capabilities designed but awaiting their game |

## Building

```
dotnet build
dotnet test
dotnet format --verify-no-changes
```

With coverage, gated at the studio floor of 80% line coverage over `Capsule.Core`:

```
dotnet test -p:CollectCoverage=true "-p:Include=[Capsule.Core]*" -p:CoverletOutputFormat=cobertura -p:Threshold=80 -p:ThresholdType=line -p:ThresholdStat=total
```

To restore exactly the committed dependency set — as CI does:

```
dotnet restore --locked-mode
```

The pre-commit hook mirrors the CI gates. Activate it once per clone:

```
git config core.hooksPath hooks
```

## Testing boundary

The device line. Above it, `Capsule.Core`'s contracts are asserted directly and `Capsule.Tests`
covers `Capsule.Runtime`'s builder validation and public surface. Below it, `Capsule.Runtime`'s
window-and-device paths need a real graphics device, so a consuming game's verify run covers them.
