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
token  := key | "Pad." pad-button | "Axis." axis '=' value
```

- A token names a member of `Key`, `PadButton` or `PadAxis` by its enum name. `None` names no
  token on any of them.
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

`Down`, `Up` and `Axis` edit the held state and emit nothing of their own, so a chord is several
`Down` calls before one `Wait`. `Tap` emits exactly one step with the key or button held on top of
the held state; tapping something already held is refused, because it would read as a release.

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

`WithInputRecording` records the snapshot the scheduler handed to each fixed step — what the
simulation actually saw — and writes the tape when the run ends, overwriting the file. The host's
Alt+Enter fullscreen chord is withheld from the simulation and so never enters a tape.

`WithInputTape` replaces device sampling entirely: one snapshot per fixed step whatever the frame
rate, and the run exits itself once the tape's last step has run. `WithInputTape(InputTape)` takes
a tape in hand; `WithInputTape(string)` reads the file at build time and throws there if it is
malformed.

The two combine: recording a replay writes back the tape it replayed, which is how a recording is
checked for having captured the run.

## Running headlessly

`RunHeadless` runs a tape through the same scene host a windowed run drives — scene transitions,
the random seed, bindings, the fixed step and scene defaults all apply — with no MonoGame, no
window, no graphics device and no texture residency:

```csharp
HeadlessRunResult result = CapsuleBoot.Configure("My Game")
    .WithRandomSeed(7)
    .RunHeadless<MainMenu>(tape);
```

One fixed step runs per tape entry until the tape is spent or game logic requests exit. The result
carries the steps run, whether the game asked to exit, and the last step's `RenderMetrics` —
render intent counted rather than drawn. `RunHeadless(string sceneName, ...)` runs a scene named
by its document instead of by its class.

`RunHeadless` writes no crash log: an exception escaping the run reaches the caller, which is a
test or a CI job.

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
CI harness — references `JAG.Capsule.Runtime` and the game's logic assembly and reaches the same
builder through `CapsuleEngine.Configure(gameName, GameScenes.Registry)`.

For assertions about the world rather than the run, drive `SceneSimulation` directly and step it
over the tape: it is substrate-free, so a test holds the scene and reads its entities between
steps.
