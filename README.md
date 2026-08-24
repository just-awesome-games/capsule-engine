<p align="center">
  <img src="docs/assets/capsule-hero.png" alt="Capsule — a game world inside a capsule" width="720">
</p>

<h1 align="center">Capsule Engine</h1>

<p align="center">A code-first C# game engine — the whole game in one capsule, the machinery sealed inside.</p>

---

Capsule is JAG Studios' in-house engine: 2D, pixel-art, deterministic, code-first. It is an
**opinionated application runtime** — it owns the frame (loop, clock, window, input, the
sim/render seam, the determinism contract). It is a library, not an application: a game brings its own
`Program.cs` and hands the host a simulation. No editor, no scene format, no project wizard.

- **Everything is C# and text on disk** — the whole surface reachable by a person and an agent alike.
- **Gameplay is pure by construction.** A simulation advances one fixed step at a time, reads input
  as named actions, never touches a graphics device, and so is assertable headlessly.
- **MonoGame is an implementation detail.** `Capsule.Runtime` marks its compile assets private, so a
  `Microsoft.Xna.Framework` using in a consuming game does not compile, while MonoGame's managed and
  native libraries still reach that game's output. Swapping the backend is engine-side only.

## The shape

`Capsule.Core` holds the pure contracts a game codes against — `ISimulation`, input, render
intent — and carries no package references at all, so it cannot reach a device even by accident.
`Capsule.Runtime` is the host: window, graphics device, clock, keyboard and gamepad, renderer, crash log, and
the only project that references MonoGame. `Capsule.Tests` runs xUnit specs over Core, plus
builder validation and a reflection guard over Runtime's public surface.

Dependencies point one way and each direction is held mechanically, not by review: Core
references nothing, Runtime references Core, and a game's logic references `Capsule.Core` while
only its one-file shell references `Capsule.Runtime`. That split is the compiler-enforced
guarantee that gameplay stays pure and headless-testable.

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

`MyGameSimulation` implements `ISimulation`: it advances one fixed step at a time, reads input as
named actions, sets `ExitRequested` when it wants to stop, and exposes what to draw as a
`FrameView`. It never touches a graphics device.

Games consume the engine as a **sibling clone, by project reference** — no packaging, no feed, no
version dance:

```
Development/
  capsule-engine/
  my-game/
```

```
git clone https://github.com/just-awesome-games/capsule-engine.git   # beside the game repo
```

```xml
<!-- MyGame.Systems.csproj — game logic -->
<ProjectReference Include="..\..\capsule-engine\Capsule.Core\Capsule.Core.csproj" />

<!-- MyGame.csproj — the one-file shell -->
<ProjectReference Include="..\..\capsule-engine\Capsule.Runtime\Capsule.Runtime.csproj" />
```

A game's CI reproduces the layout by checking the engine out beside it at a pinned ref.

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

Above the device line, `Capsule.Core`'s contracts are asserted directly and `Capsule.Tests`
covers `Capsule.Runtime`'s builder validation, deadzone filtering and public surface. Below it, the window-and-device
paths need a real graphics device, so a consuming game's verify run covers them.

## Further reading

[`AGENTS.md`](AGENTS.md) is the rules any contributor — human or agent — works under here.
[`docs/architecture.md`](docs/architecture.md) carries the determinism contract and the
capabilities designed but awaiting their game. Everything else is in the code.
