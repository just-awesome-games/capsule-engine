# Getting started

After this page you have your own Capsule game on screen: a window, a scene, a camera, and one
entity you can move with the keyboard.

## Run the sample first

Install the .NET SDK that [`global.json`](../global.json) selects, then:

```text
git clone https://github.com/just-awesome-games/capsule-engine.git
cd capsule-engine
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
```

Move with A and D, jump with Space, shoot with the left mouse button, pause with Escape. Press
`` ` `` for the development overlay ([`debugging.md`](debugging.md)).
[`samples/MinimalGame/`](../samples/MinimalGame/) is a complete game in the shape described below,
and the fastest start for a game of your own is to copy it.

## Three projects

| Project | Role | What it holds |
| --- | --- | --- |
| `src/MyGame.Game` | `<CapsuleGameLogic>true</CapsuleGameLogic>` | Scenes, entities, components, the `Assets/` authoring tree. It cannot reach a device, a file or a clock. |
| `src/MyGame.Shell` | `<CapsuleGameShell>true</CapsuleGameShell>` | The executable. One file, the entry point below. |
| `tests/MyGame.Tests` | no role | Headless tests of the logic project ([`testing.md`](testing.md)). |

The project files, the version pin and the two solution-wide MSBuild files are in
[`build-and-publish.md`](build-and-publish.md). The rest of this page is the C# those projects hold.

## The shell

The shell's only hand-written code is its entry point, against the generated `CapsuleBoot`:

```csharp
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using MyGame;
using MyGame.Scenes;

try
{
    return CapsuleBoot.Configure("My Game", new DesktopPlatform())
        .WithCommandLine(args)
        .WithRunStart(GameBoot.Start)
        .RunScene<PlayField>();
}
catch (CommandLineException failure)
{
    return failure.Report();
}
```

`CapsuleBoot` is generated into the shell project from its role. Every generated class lives in
`Capsule.Generated`. The build imports it into the logic and shell projects, and a test project writes
`using Capsule.Generated;` itself. `WithCommandLine(args)` installs the engine's standard command line
and throws `CommandLineException` for `--help` and for a flag it refuses, which the `catch` turns into
the process's exit code. Every other boot lever is on `EngineBuilder`, and each documents its default.

## The game's actions

An action names what the player can do, apart from the device that does it. Declare them once at the
assembly root and bind them in one place:

```csharp
using Capsule.Input;

namespace MyGame;

public static class GameInput
{
    public static readonly AxisAction Move = new("move");

    public static void Configure(InputConfiguration input)
    {
        input.Bindings.BindAxis(Move, Key.A, Key.D);
        input.Bindings.BindAxis(Move, PadAxis.LeftStickX);
    }
}
```

More on actions, axes and playing a run with no one at the keyboard is in [`input.md`](input.md).

## A scene with one entity

A scene is one world: its contents and a camera. A camera spans world units, and one that spans
nothing draws nothing, so the scene sets its span:

```csharp
using System.Numerics;
using Capsule.Scenes;
using MyGame.Entities;

namespace MyGame.Scenes;

public sealed class PlayField : Scene
{
    public PlayField()
    {
        Camera = new Camera { ViewportSize = new Vector2(320f, 180f) };
        Add(new Blob(new Vector2(160f, 90f)));
    }
}
```

An entity is a place in the world, and what it looks like is a renderer component on it:

```csharp
using System.Numerics;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using MyGame;

namespace MyGame.Entities;

public sealed class Blob : Entity
{
    private const float Speed = 60f;

    public Blob(Vector2 position)
        : base(position) =>
        Add(new ColorRect(new Vector2(16f, 16f)));

    protected override void OnStep(in StepContext context) =>
        Position += new Vector2(context.Input.Axis(GameInput.Move) * Speed * context.DeltaSeconds, 0f);
}
```

`OnStep` runs on the fixed step, sixty times a simulated second by default, so `DeltaSeconds` is
constant and the run is reproducible. `Position` is the entity's top-left corner in world units, and
`ColorRect` draws its size from there.

## Run it

```text
dotnet run --project src/MyGame.Shell
dotnet run --project src/MyGame.Shell -- --scene PlayField
dotnet run --project src/MyGame.Shell -- --headless --driver Walkthrough
dotnet test
```

## Where to go next

- [`input.md`](input.md): actions, axes, the pointer, input drivers, the standard command line.
- [`rendering.md`](rendering.md): the canvas, cameras, draw order, sprites, text, parallax.
- [`audio.md`](audio.md): clips, buses, sources, the mixer.
- [`collision.md`](collision.md): colliders, layers, contacts, moving a body, queries.
- [`assets.md`](assets.md): textures, sprite sheets, atlases, audio, fonts, and asset keys.
- [`scenes.md`](scenes.md): the authoring model and the `*.scene.json` format.
- [`persistence.md`](persistence.md): save documents and where they go.
- [`build-and-publish.md`](build-and-publish.md): project wiring, build properties, publishing.
- [`testing.md`](testing.md): which boundary to test a game at.
- [`debugging.md`](debugging.md): the development overlay, debug draw, panels, logging.
- [`architecture.md`](architecture.md): modules, the logic boundary, the determinism contract.
