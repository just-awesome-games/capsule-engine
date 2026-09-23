# Getting started

After this page you have your own Capsule game on screen: a window, a scene, a camera, and one
entity you can move with the keyboard.

## Run the sample first

Clone the engine and run the sample as the [README](../README.md#quick-start) shows. Its controls are in
the [sample's README](../samples/MinimalGame/README.md#controls).
[`samples/MinimalGame/`](../samples/MinimalGame/) is a complete game in the shape described below. The
fastest start for a game of your own is a copy of it.

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
`using Capsule.Generated;` itself. `WithCommandLine(args)` installs the engine's standard command line,
and the `catch` reports `--help` or a refused flag ([`input.md`](input.md#the-standard-command-line)).
Every other boot lever is on `EngineBuilder`, and each documents its default.

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

A scene is one world: its contents and a camera. A camera draws nothing until its `ViewportSize` spans
some world units:

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

`OnStep` runs on the fixed step, sixty times a simulated second by default. `DeltaSeconds` is constant,
and the run is reproducible. `Position` is in world units with Y pointing down, and `ColorRect` draws its
`Size` down and to the right of it.

## Run it

```text
dotnet run --project src/MyGame.Shell
dotnet run --project src/MyGame.Shell -- --scene PlayField
dotnet run --project src/MyGame.Shell -- --headless --driver Walkthrough
dotnet test
```

## Where to go next

The [README](../README.md#documentation) indexes every task page.
