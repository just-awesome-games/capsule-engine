# Capsule.Runtime

The host. It owns the window, the graphics device, the clock and the keyboard, and it is the
only project in the engine that references MonoGame.

A game's shell references this project; its game logic must not.

## The builder

`CapsuleEngine.Configure()` returns an `EngineBuilder`. Every `With` method validates eagerly
and returns the builder, so a misconfiguration throws at the call site that caused it rather
than somewhere inside the loop. `Run` blocks until the game exits.

| Method | Effect | Default | Rejects |
| --- | --- | --- | --- |
| `WithWindow(title, width, height, resizable)` | Window title, back-buffer size, user resizing | `"Capsule"`, 1280×720, not resizable | An empty title; a non-positive dimension |
| `WithFixedStep(hertz)` | Simulation steps per second of simulated time | 60 | A non-positive rate |
| `WithClearColor(color)` | Colour the frame is cleared to | `ColorRgba.Black` | — |
| `WithCrashLog(appName)` | Enables crash logging under that application name | Off | An empty name, or one that is not a single safe directory name: separators, invalid filename characters, `.` and `..`, a Windows reserved device name (`CON`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, with or without an extension), or a trailing dot or space |
| `WithBindings(configure)` | Registers action bindings; call it repeatedly and they accumulate | No bindings | A null configurator |
| `Run(simulation)` | Opens the window and runs until exit | — | A null simulation |

Everything has a working default except the simulation, which is the one thing the engine
cannot invent.

## The loop contract

`IsFixedTimeStep = false`: MonoGame's own fixed step does not give a harness the frame-exact
control its determinism contract needs, so the runtime owns an accumulator instead.

Per frame:

1. **Sample the keyboard once** into a `DeviceSnapshot` and hand it to the `SnapshotLatch`.
2. Add the frame's elapsed time to the accumulator, **clamped at 0.25 s**. Without the clamp a
   long stall — a breakpoint, a window drag — queues more steps than the next frame can run and
   the accumulator never drains.
3. While the accumulator holds a whole step: advance `InputState` against the latch's
   `ConsumeStepSnapshot()`, `Step` the simulation, subtract the step, and stop if the simulation
   raised `ExitRequested`.

Sampling happens on every frame, **including one that drains no step** — that is the whole reason
the latch exists. At render rates above the step rate most frames drain nothing, and a sample
thrown away there loses a key that was pressed and released before the next step ran. The latch
carries those keys forward, and conversely reports the same snapshot to every step drained inside
one frame, so an edge still fires exactly once either way. The semantics are the latch's, in
`Capsule.Core`; the runtime only observes and consumes.

`Draw` computes the interpolation alpha — the fraction of a step not yet simulated, in [0, 1)
because `Update` drains the accumulator below one step — and passes it to the renderer.
Nothing moves yet, so nothing reads it beyond the signature; the shape is there so that adding
interpolated motion is not a loop rewrite.

The simulation is single-threaded. There are no worker threads and no `async` anywhere in the
step path.

## Rendering

The renderer clears to the configured colour and draws each `TextBlock` in the simulation's
`FrameView` with a 1×1 white texture under `SamplerState.PointClamp`, so cells stay crisp at
any scale. `ColorRgba` is straight alpha and the studio blend convention is premultiplied, so
colours convert through `Color.FromNonPremultiplied` on the way to the device.

`Anchor` dispatch is an exhaustive switch with no discard arm: adding an `Anchor` value without
a placement fails the build. The same technique maps `Key` to its backend key.

## The MonoGame-hiding contract

**Do not "tidy" the `PrivateAssets` metadata on the MonoGame `PackageReference`.** It reads:

```xml
<PackageReference Include="MonoGame.Framework.DesktopGL" Version="3.8.5.1">
  <PrivateAssets>compile;contentfiles;analyzers;build</PrivateAssets>
</PackageReference>
```

That is NuGet's default private set plus `compile`, and `compile` is the load-bearing entry.
`PrivateAssets` names the asset kinds that do **not** flow to consumers, so:

- **Compile assets stop here.** A game referencing `Capsule.Runtime` cannot write a
  `Microsoft.Xna.Framework` using — the namespace does not exist as far as its compiler is
  concerned. Backend leakage becomes a build error rather than a code-review habit.
- **Runtime and native assets still flow.** MonoGame's managed assembly and its native SDL and
  OpenAL libraries reach the consuming game's output and `deps.json`, so the game actually
  runs.

Remove `compile` and the engine still builds, the game still runs, and the guarantee is gone
silently. That is precisely why it is written down here.

The second half of the contract is a rule no project setting can hold: **no MonoGame type in
any public or protected signature of this project.** Everything MonoGame-shaped —
`CapsuleGame`, `FrameRenderer`, `KeyboardSampler`, `EngineOptions`, `CrashLog` — is `internal`.
`Capsule.Tests` reflects over this assembly's exported surface and fails if a MonoGame type
appears in one; it also, by referencing this project and compiling without any MonoGame
reference available, serves as a live probe of the first half.

## Crash logging

`WithCrashLog(appName)` wraps the run. An escaping exception is written to `crash.log` under
the OS-local application data folder for that name — `%LOCALAPPDATA%\<appName>` on Windows —
and then rethrown, so the exit code and the debugger break survive. The file is overwritten,
not appended: the latest crash is the one that matters, and the file stays bounded. Local
application data rather than the executable's directory, because install locations are often
read-only. A failed log write is swallowed; it must never mask the original exception.

Without `WithCrashLog`, a windowed build has no console and an escaping exception vanishes
without trace.
