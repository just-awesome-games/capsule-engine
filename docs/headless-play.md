# Headless play

A Capsule run is determined by its initial state, its fixed step, and the sequence of
`DeviceSnapshot` values the simulation sees. That sequence comes from a class — an input driver — so
a run is played with no window, no graphics device and no person at the keyboard, and plays the same
way every time.

## Input drivers

`Capsule.Scenes.Input.IInputDriver` has one method:

```csharp
bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot);
```

The driver is asked once per fixed step, before that step runs, whatever the frame rate; `scene` is
the scene about to be stepped, so a driver reads the world it is playing and a transition hands it
the new scene. Returning false ends the run, and the step it declined never runs. A driver that ends
the run on a condition of its own presses whatever key the game exits on instead.

Everything a driver measures is counted in fixed steps, never in seconds. At the default 60 Hz, one
second of play is 60 steps.

A driver with a public parameterless constructor is registered by the build under its class name,
which is what `--driver` takes. One that takes constructor arguments registers under no name and
reaches a run through `WithInputDriver` or `RunHeadless`.

## Scripting a driver

`Capsule.Scenes.Input.InputScript` builds a driver of a fixed sequence the way a device produces
one: a held state that edits change, and calls that emit steps of it.

```csharp
public sealed class Walkthrough : IInputDriver
{
    private readonly IInputDriver _script = new InputScript()
        .Wait(30)                             // 30 idle steps
        .Down(Key.D)                          // held from the next emitted step on
        .Wait(60)                             // 60 steps walking right
        .Tap(Key.Space)                       // one step with Space down, then up again
        .Wait(60)
        .Up(Key.D)
        .Axis(PadAxis.LeftStickX, -1f)
        .Wait(30)
        .Build();

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot) =>
        _script.TryNext(scene, tick, out snapshot);
}
```

## Writing a driver by hand

A driver that reacts to the game reads the scene it is handed:

```csharp
public sealed class ReachTheDoor : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = scene switch
        {
            Hall hall when hall.Player.Position.X < 240f => DeviceSnapshot.Of(Key.D),
            Hall => DeviceSnapshot.Of(Key.Space),
            _ => DeviceSnapshot.Empty,
        };

        // A ceiling on the run, so a game that never reaches the door still ends.
        return tick < 600;
    }
}
```

## The standard command line

Capsule owns the flags that drive input, headless play and frame timing, so a game never writes a
parser for them. One call hands the process arguments over, and `RunScene` returns the exit code:

```csharp
return CapsuleBoot.Configure("My Game").WithCommandLine(args).RunScene<MainMenu>();
```

Nothing is read ambiently: a shell that does not pass `args` has no command line at all.

| Flag                       | Effect                                                             |
| -------------------------- | ------------------------------------------------------------------ |
| `--driver <Name>`          | Drives the run from the input driver of that class name.           |
| `--headless`               | Runs with no window, which needs a driver.                         |
| `--frames <csv> [seconds]` | Writes host frame timing, exiting after `seconds` when given.      |
| `--help`                   | Prints the usage block on standard output and exits.               |

Flags combine, every value is required, and repeating one is an error. A game with flags of its own
removes them before handing the rest over, since anything Capsule does not declare is rejected.

`--driver X` alone opens the window and plays the driver in it; `--headless --driver X` opens no
window at all. `RunScene` returns 2 for a rejected command line, a driver name nothing answers to —
reported with the names that are registered — or `--headless` with no driver; everything else
returns 0.

Drivers are discovered wherever the game declares them: the shell project, the logic project, or any
logic assembly the shell references.

## Running headlessly from a test

`RunHeadless` runs a driver through the same scene host a windowed run drives, with no MonoGame, no
window, no graphics device and no texture residency:

```csharp
IInputDriver driver = new InputScript().Tap(Key.Space).Wait(120).Tap(Key.Escape).Build();

HeadlessRunResult result = CapsuleEngine.Configure("My Game", GameScenes.Registry)
    .WithRandomSeed(7)
    .RunHeadless<FirstRoom>(driver);

Assert.True(result.ExitRequested);
Assert.Equal(122, result.Steps);
```

`CapsuleBoot` is generated into the shell, so a project that is not the shell — a test project, a
CI harness — references `JAG.Capsule.Runtime` and the game's logic assembly and enters through
`CapsuleEngine.Configure(gameName, GameScenes.Registry)`, which returns the same builder. That
overload registers no driver names, which is what a caller passing its own driver wants.

`RunHeadless` takes the driver itself and replaces anything `WithInputDriver` set.

## Screenshots

Game logic cannot write a file, so a screenshot is an intent the scene raises and the host fulfils
on its next drawn frame. Bind an action, call `CaptureFrame` on the press, and press the key from a
driver:

```csharp
protected override void OnStep(in StepContext context)
{
    if (context.Input.WasPressed(Screenshot))
    {
        CaptureFrame("shots/room.png");
    }
}
```

```csharp
IInputDriver driver = new InputScript().Wait(60).Tap(Key.F12).Wait(1).Build();
```

A windowed run under that driver writes the PNG. A headless run has no surface to save, so it clears
the request and writes nothing.

For assertions about the world rather than the run, drive `SceneSimulation` directly and step it
over the snapshots: it is substrate-free, so a test holds the scene and reads its entities between
steps.
