# Testing a game

Capsule's simulation is substrate-free and deterministic, so a game's behaviour is testable in an
ordinary test project: no window, no graphics device, no editor and no play mode. This page says
what exists for that and which to reach for; each type's XML documentation carries its contract.

## What Capsule does not ship

No test framework, no assertion library, no fixture lifetime, no scene content, and no helper that
knows what a game's world means. A game chooses its own runner and writes its own assertions and
fixtures; Capsule's job is to make one step, one run and one snapshot sequence reproducible and
reachable, and everything above that is a game's own and would carry its assumptions into the
engine.

## Stepping a scene: `SceneRun`

`Capsule.Scenes.SceneRun` plays the host's role over one scene: it owns the step tick and the run's
`InputState`, and steps, runs, plays a driver or runs until a condition holds.

```csharp
using Capsule.Input;   // DeviceSnapshot, InputState, Key
using Capsule.Scenes;  // SceneRun, SceneSimulation

using SceneRun run = new(new FirstRoom(), new InputState(GameInput.Bindings));

run.Run(30);                            // 30 steps with nothing held
run.Run(20, DeviceSnapshot.Of(Key.D));  // 20 walking right

Assert.True(run.Scene.FindSingle<Player>().Position.X > 240f);
```

Reach for it whenever the assertion is about the world. Step `SceneSimulation` directly only for a
single step, or to shape a `StepContext` the test is deliberately controlling — a test that builds
its own contexts owns the tick and the input state and can get either wrong.

## Driving input: `DeviceSnapshot`, `InputScript` and `IInputDriver`

A step's input is a `DeviceSnapshot`. One snapshot covers the simple case; a sequence is either
scripted with `Capsule.Scenes.Input.InputScript` or produced by an `IInputDriver` written as a
class, which reads the scene it is playing. [`headless-play.md`](headless-play.md) covers writing
one and naming it on the command line.

## Running the whole host: `CapsuleEngine.RunHeadless`

`RunHeadless` runs a driver through the same scene host a windowed run drives, with no MonoGame, no
window, no graphics device and no media loading:

```csharp
using Capsule.Input;         // Key
using Capsule.Runtime;       // CapsuleEngine, HeadlessRunResult
using Capsule.Scenes.Input;  // IInputDriver, InputScript

IInputDriver driver = new InputScript().Tap(Key.Space).Wait(120).Tap(Key.Escape).Build();

HeadlessRunResult result = CapsuleEngine.Configure("My Game", GameScenes.Registry)
    .WithRandomSeed(7)
    .RunHeadless<FirstRoom>(driver);

Assert.True(result.ExitRequested);
```

Choose it over `SceneRun` when the run itself is what is under test — scene transitions, boot
configuration, asset wiring, the exit code, how many steps a driver actually drove. It answers with
a `HeadlessRunResult` rather than a live scene, and it lives in `JAG.Capsule.Runtime`: a test
project referencing only `JAG.Capsule` cannot call it, and that is the usual reason to stay on
`SceneRun`.

## Reproducibility: `RandomSource`

A scene's `Random` is a seeded `RandomSource`, so a run repeats exactly; `SceneSimulation` takes one
and `WithRandomSeed` sets the host's. The determinism it is one half of is in
[`architecture.md`](architecture.md).

## Collision without a scene

`CollisionWorld2D` is built and queried directly, so shapes, rays, overlaps, casts and the mover are
testable with no scene, no entity and no step at all. Reach for it when what is under test is
geometry rather than behaviour over time.
