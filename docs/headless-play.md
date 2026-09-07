# Headless play

A Capsule run is determined by its initial state, its fixed step, and the sequence of
`DeviceSnapshot` values the simulation sees. That sequence is a first-class value — an
`InputTape` — so a run can be recorded, replayed, and driven with no window, no graphics device
and no person at the keyboard.

## Input tapes

`Capsule.Input.InputTape` is an immutable sequence holding exactly one `DeviceSnapshot` per fixed
step. Tapes compare by content, so a replay's recording can be asserted equal to the tape it
replayed.

Everything a tape measures is counted in fixed steps, never in seconds. At the default 60 Hz, one
second of play is 60 steps.

## Scripting a tape

`Capsule.Input.InputScript` builds a tape the way a device produces one: a held state that edits
change, and calls that emit steps of it.

```csharp
InputTape tape = new InputScript()
    .Wait(30)                             // 30 idle steps
    .Down(Key.D)                          // held from the next emitted step on
    .Wait(60)                             // 60 steps walking right
    .Tap(Key.Space)                       // one step with Space down, then up again
    .Wait(60)
    .Up(Key.D)
    .Axis(PadAxis.LeftStickX, -1f)
    .Wait(30)
    .Build();
```

## The tape file

A recorded tape is a small binary file: a four-byte magic, a version, and then one fixed-size
record per step holding exactly what a `DeviceSnapshot` holds — nothing compressed, nothing
run-length encoded, every field little-endian. It is written and read only by the engine. Reading
one that is not a Capsule tape, is of a version this engine does not read, ends mid-step, or holds
a record no device could have reported — a bit no member names, or an axis outside its range —
raises `Capsule.Runtime.InputTapeFormatException`, whose message says which.

The file is not a format to author in. A tape written by hand is written in code, with
`InputScript`.

## Recording and replaying a run

Both are builder configuration, so any run — windowed or headless — takes them:

```csharp
CapsuleBoot.Configure("My Game")
    .WithInputRecording("run.tape")       // write what every fixed step consumed, on exit
    .RunScene<MainMenu>();

CapsuleBoot.Configure("My Game")
    .WithInputTape("run.tape")            // drive the run from it instead of the devices
    .RunScene<MainMenu>();
```

The two combine: recording a replay writes back the tape it replayed, which is how a recording is
checked for having captured the run.

## The standard command line

Capsule owns the flags that drive recording, replay, headless play and frame timing, so a game
never writes a parser for them. One call hands the process arguments over, and `RunScene` returns
the exit code:

```csharp
return CapsuleBoot.Configure("My Game").WithCommandLine(args).RunScene<MainMenu>();
```

Nothing is read ambiently: a shell that does not pass `args` has no command line at all.

| Flag                       | Effect                                                            |
| -------------------------- | ------------------------------------------------------------------ |
| `--record <tape>`          | Writes the snapshot every fixed step consumed, on exit.            |
| `--replay <tape>`          | Drives the run from a tape file instead of the devices.            |
| `--headless <tape>`        | Runs the tape with no window; the exit code is the run's.          |
| `--frames <csv> [seconds]` | Writes host frame timing, exiting after `seconds` when given.      |
| `--help`                   | Prints the usage block on standard output and exits.               |

Flags combine, every value is required, and repeating one is an error. A game with flags of its own
removes them before handing the rest over, since anything Capsule does not declare is rejected.

`RunScene` returns 2 for a rejected command line or a tape file it cannot read, reporting the defect
and the usage block on standard error; a headless run that neither spent its tape nor was asked to
exit returns 1; everything else returns 0.

## Running headlessly

`RunHeadless` runs a tape through the same scene host a windowed run drives, with no MonoGame, no
window, no graphics device and no texture residency:

```csharp
HeadlessRunResult result = CapsuleBoot.Configure("My Game")
    .WithRandomSeed(7)
    .RunHeadless<MainMenu>(tape);
```

## Driving a game without a keyboard

An agent or a CI job that needs to see what a change does to a real run authors the tape in code
and runs it headlessly, with no file in between:

```csharp
InputTape tape = new InputScript().Tap(Key.Space).Wait(120).Tap(Key.Escape).Build();

HeadlessRunResult result = CapsuleEngine.Configure("My Game", GameScenes.Registry)
    .WithRandomSeed(7)
    .RunHeadless<FirstRoom>(tape);

Assert.True(result.ExitRequested);
Assert.Equal(tape.Count, result.Steps);
```

`CapsuleBoot` is generated into the shell, so a project that is not the shell — a test project, a
CI harness — references `JAG.Capsule.Runtime` and the game's logic assembly and enters through
`CapsuleEngine.Configure(gameName, GameScenes.Registry)`, which returns the same builder.

A tape recorded from a play session is replayed through `WithInputTape(path)` on a windowed run;
`RunHeadless` takes the tape itself and replaces anything `WithInputTape` set.

## Screenshots

Game logic cannot write a file, so a screenshot is an intent the scene raises and the host fulfils
on its next drawn frame. Bind an action, call `CaptureFrame` on the press, and press the key from a
tape:

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
InputTape tape = new InputScript().Wait(60).Tap(Key.F12).Wait(1).Build();
```

A windowed run replaying that tape writes the PNG. A headless run has no surface to save, so it
clears the request and writes nothing.

For assertions about the world rather than the run, drive `SceneSimulation` directly and step it
over the tape: it is substrate-free, so a test holds the scene and reads its entities between
steps.
