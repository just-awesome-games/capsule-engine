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

## Tape text

`ToText()` writes a tape as line-oriented, run-length encoded text and `Parse` reads it back
exactly, axis values included. One line describes a run of identical consecutive steps, so an idle
minute is one line:

```text
# stand still, then walk right and jump
3600
2 W Pad.South Axis.LeftStickX=-0.3
1 Escape Pad.South Axis.LeftStickX=-0.3
```

The grammar is:

```text
line   := repeat (' ' token)*
repeat := a decimal count of consecutive identical steps, at least 1
token  := key | pad-button | "Axis." axis '=' value
key    := a `Key` name, or "Key." and the decimal value of one the enum does not name
pad-button := "Pad." and either a `PadButton` name or the decimal value of one the enum does not name
```

- A token names a member of `Key`, `PadButton` or `PadAxis` by its enum name. `None` names no
  token on any of them.
- A `DeviceSnapshot` also holds values below its capacity that the enums do not name, so those are
  written and read in their decimal form — `Key.127`, `Pad.31`.
- An axis at rest is written as no token, and an axis value is written as the shortest text that
  round-trips it, in the invariant culture.
- A line with a repeat count and no tokens is that many idle steps.
- Written tokens are ordered keys, then pad buttons, then axes, each in enum order, so the same
  tape always writes the same bytes and two tapes diff line by line. Tokens are read in any order.
- Blank lines, and lines whose first non-blank character is `#`, are ignored. Lines end with `\n`.

A malformed line raises `FormatException` naming its 1-based number.

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

## Running headlessly

`RunHeadless` runs a tape through the same scene host a windowed run drives, with no MonoGame, no
window, no graphics device and no texture residency:

```csharp
HeadlessRunResult result = CapsuleBoot.Configure("My Game")
    .WithRandomSeed(7)
    .RunHeadless<MainMenu>(tape);
```

## Driving a game without a keyboard

An agent or a CI job that needs to see what a change does to a real run:

1. Record a play session once, by hand or from a script:
   `.WithInputRecording("assets/tapes/first-room.tape")` on the windowed run.
2. Read the tape. It is short, human-readable text — trim it to the part that matters, or write it
   by hand with `InputScript` in the first place.
3. Replay it headlessly and assert the result:

```csharp
InputTape tape = InputTape.Parse(File.ReadAllText("assets/tapes/first-room.tape"));

HeadlessRunResult result = CapsuleBoot.Configure("My Game")
    .WithRandomSeed(7)
    .RunHeadless<FirstRoom>(tape);

Assert.True(result.ExitRequested);
Assert.Equal(tape.Count, result.Steps);
```

`CapsuleBoot` is generated into the shell, so a project that is not the shell — a test project, a
CI harness — references `JAG.Capsule.Runtime` and the game's logic assembly and enters through
`CapsuleEngine.Configure(gameName, GameScenes.Registry)`, which returns the same builder.

For assertions about the world rather than the run, drive `SceneSimulation` directly and step it
over the tape: it is substrate-free, so a test holds the scene and reads its entities between
steps.

## Reading what happened

### The state trace

`.WithStateTrace(path)` records the world at the end of every fixed step and writes it as one CSV
when the run ends. One trace spans every scene the run passes through, so a transition does not
restart the tick count or the file:

```csharp
CapsuleEngine.Configure("My Game", GameScenes.Registry)
    .WithRandomSeed(7)
    .WithStateTrace("artifacts/first-room.csv")
    .RunHeadless<FirstRoom>(tape);
```

Rows are long-form — one value per line — so two runs diff line by line and any reader pivots them:

| Column | Holds |
| --- | --- |
| `tick` | The fixed step the row was taken at the end of; 0 is the first step of the run. |
| `subject` | What the row is about: `#12` for an entity spawned from that document placement, `e3` for one created in code, `camera` for the scene's camera. |
| `column` | What is being recorded: `x`, `y` and `type` for every entity, `x` and `y` for the camera, plus whatever the subject's own trace sources write. |
| `value` | The value, in the invariant culture; floats in the shortest text that round-trips. |

```text
tick,subject,column,value
0,#12,x,64.5
0,#12,y,112
0,#12,type,Player
0,#12,clip,run
0,#12,frame,2
0,camera,x,64.5
```

An entity or component implementing `Capsule.Scenes.ITraceSource` adds its own columns under its
subject; `SpriteAnimator` already writes `clip` and `frame`.

### Frame captures

`.WithFrameCapture(directory, ticks)` saves the frame drawn after each named tick as
`frame-<tick>.png`. It needs a device, so it is a windowed run only — a headless run has no
surface to save:

```csharp
CapsuleBoot.Configure("My Game")
    .WithRenderResolution(480, 270)
    .WithInputTape("assets/tapes/first-room.tape")
    .WithFrameCapture("artifacts/frames", 0, 60, 240)
    .RunScene<FirstRoom>();
```

With a render resolution declared the image is that surface, ahead of the letterbox blit, so its
size is the same whatever the window is doing.
