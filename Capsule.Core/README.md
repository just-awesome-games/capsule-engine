# Capsule.Core

The contracts a game codes against. Pure: no package references, no I/O, no clock, no device —
so every type here is constructible in a unit test and every behaviour here is assertable
without opening a window.

Game logic references this project and nothing else of Capsule's.

## ISimulation

```csharp
public interface ISimulation
{
    void Step(in StepContext context);
    bool ExitRequested { get; }
    FrameView View { get; }
}
```

A simulation owns all game state and advances it one fixed step at a time. It never reads
wall-clock time — `context.DeltaSeconds` is the only time it sees — and it never draws.

`ExitRequested` is a latch the simulation raises; the runtime reads it after every step and
shuts down. The runtime never clears it.

`View` is read every frame, so return a held instance. See [Render intent](#render-intent).

### StepContext

```csharp
public readonly struct StepContext(double deltaSeconds, InputState input, long tick)
```

Everything the runtime hands the simulation for one step, in one value. It exists so a new
per-step channel is a new member here rather than a new parameter on `Step` — extensible by
addition, without a signature break in every game.

| Member | Meaning |
| --- | --- |
| `DeltaSeconds` | Simulated seconds this step represents; constant for a given configuration |
| `Input` | Action-level input for this step |
| `Tick` | Index of this step; `0` on the first step ever delivered |
| `TotalSeconds` | Simulated seconds at the **start** of this step — `Tick * DeltaSeconds` |

Time here is simulated, never wall clock: `TotalSeconds` is derived from the tick count rather
than accumulated, so a long run cannot drift, and a harness replaying the same tick sequence
sees the same times.

## Input

Named actions over snapshot-derived edges. Four types, in dependency order:

| Type | Role |
| --- | --- |
| `Key` | Engine-owned physical key. `Key.None` is the default and is never a member of a set |
| `DeviceSnapshot` | The set of keys held at one instant. A value over a fixed bitset — no heap, no aliasing |
| `SnapshotLatch` | Folds the frames sampled since the last step into the snapshot that step consumes |
| `InputAction` | A named thing the player can do. `readonly record struct`, equal by name |
| `ActionBindings` | Which keys stand for which actions. Written at configuration time, read every step |
| `InputState` | Action-level input for the current step, derived from two consecutive snapshots |

A game declares its actions once and binds them at startup:

```csharp
public static class MyGameActions
{
    public static readonly InputAction Quit = new("Quit");
    public static readonly InputAction Jump = new("Jump");
}

// in Program.cs
.WithBindings(bindings => bindings
    .Bind(MyGameActions.Quit, Key.Escape)
    .Bind(MyGameActions.Jump, Key.Space, Key.W))
```

Any bound key satisfies the action. Binding the same action twice unions the keys rather than
replacing them.

Then, inside the step:

```csharp
public void Step(in StepContext context)
{
    if (context.Input.WasPressed(MyGameActions.Jump)) { /* ... */ }
    if (context.Input.IsHeld(MyGameActions.Jump)) { /* ... */ }
    if (context.Input.WasReleased(MyGameActions.Jump)) { /* ... */ }
}
```

An unbound action is never held and never fires an edge.

### Edges are diffs, and that is the determinism seam

`InputState` derives everything from `(previous snapshot, current snapshot, bindings)`. No edge
comes from an OS callback, a queue, or a timestamp. Two consequences:

- **A run is reproducible.** Feed the same sequence of snapshots and the same edges fire in the
  same order. Hardware produces snapshots in the shipping game; a harness fabricates them in a
  test. Nothing downstream can tell the difference — that is the whole point of the type.
- **Sampling rate and step rate are decoupled.** The runtime samples the keyboard once per
  frame and `SnapshotLatch` reconciles that rate with the step rate, so an edge fires on exactly
  one step no matter how many frames or steps sit between the samples.

`InputState.Advance` is the runtime's to call; a simulation only reads. It is public because a
harness outside the runtime must be able to drive it.

`DeviceSnapshot` allocates nothing: it is a bitset in a `readonly struct`, and `With`, `Without`,
`Union` and `Of` return new values rather than mutating.

### SnapshotLatch

At render rates above the step rate a frame often drains no step, and its sample would simply be
discarded — a key pressed and released entirely between two consumed steps would then produce
neither a press nor a release. The latch closes that hole:

```csharp
latch.Observe(sampledSnapshot);          // once per frame, drained step or not
input.Advance(latch.ConsumeStepSnapshot());  // once per fixed step
```

`ConsumeStepSnapshot` returns the union of every frame observed since the previous step, so a key
seen down in any of them is down for that step; the latch then clears, and the following step sees
the key up. A tap between steps therefore reaches the simulation as one held tick and then a
release. When several steps drain in one frame the opposite case applies: nothing new has been
observed, so the last observed frame stands and the edge still fires only once.

A harness that owns per-tick snapshots outright can skip the latch and call `InputState.Advance`
directly — the seam is `DeviceSnapshot`, not the latch.

## Render intent

Simulations do not draw. They expose what they want on screen and the runtime draws it, which
is what keeps game logic free of a device and testable headlessly.

| Type | Role |
| --- | --- |
| `ColorRgba` | Straight (non-premultiplied) 8-bit RGBA. `Black`, `White` |
| `FrameView` | Everything to draw this frame |

`FrameView` carries no members yet — the runtime clears the frame and draws nothing, so a
simulation returns the shared empty view:

```csharp
public FrameView View => FrameView.Empty;
```

The vocabulary arrives with the first renderable feature, per the placement rule in
[`AGENTS.md`](../AGENTS.md): no engine type without a game call site for it.

`FrameView` is immutable, and that is a performance contract, not a style: **build one per
distinct visual state and hold it.** Rebuilding a view every frame allocates every frame, which
is exactly what the fixed step must not do. A view that changes with state is a field the
simulation reassigns when the state changes — not a list it refills.
