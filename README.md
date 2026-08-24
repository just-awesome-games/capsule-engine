# Capsule

JAG Studios' in-house, code-first C# game engine.

Capsule is a library, not an application: a game brings its own `Program.cs`, configures an
engine host, and hands it a simulation. There is no editor, no scene format and no project
wizard — everything is C# and text on disk, which is what makes the whole surface reachable by
both a person and an agent.

**MonoGame is an implementation detail.** Capsule runs on MonoGame today, and no game built on
Capsule can tell. `Capsule.Runtime` marks MonoGame's compile assets private, so a
`Microsoft.Xna.Framework` using in a consuming game does not compile, while MonoGame's managed
and native libraries still reach that game's output. Swapping the backend is an engine-side
change; the games do not participate.

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
git clone https://github.com/jagstudiosdev/capsule-engine.git   # beside the game repo
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
| [`AGENTS.md`](AGENTS.md) | The rules any contributor — human or agent — works under here |
| [`Capsule.Core/README.md`](Capsule.Core/README.md) | The contracts a game codes against: simulation, input, render intent |
| [`Capsule.Runtime/README.md`](Capsule.Runtime/README.md) | The builder, the loop contract, the MonoGame-hiding contract, crash logging |
| [`docs/architecture.md`](docs/architecture.md) | Project map, dependency directions, the input and render pipelines end to end, what is deliberately not built yet |

The studio's binding MonoGame standard lives in the design repo at
`studio/technical/engines/monogame/best-practices.md`.

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

`Capsule.Runtime`'s window-and-device paths are not unit-tested: they need a real graphics
device, so a consuming game's verify run is what covers them. Everything above that line is.
