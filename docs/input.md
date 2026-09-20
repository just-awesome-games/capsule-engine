# Input

After this page you can declare a game's actions, read them in a step, and play a whole run with
nobody at the keyboard.

## Declare the actions

An action is a named thing the player can do, apart from the device that does it. A game
declares its actions once at its assembly root and binds them in one place, so every scene reads
actions and not keys or pad buttons:

```csharp
public static class GameInput
{
    /// <summary>Horizontal movement, in [-1, 1].</summary>
    public static readonly AxisAction Move = new("move");

    /// <summary>Leaves the floor.</summary>
    public static readonly InputAction Jump = new("jump");

    /// <summary>Sets the gamepad deadzones and binds every action to the devices the game supports.</summary>
    public static void Configure(InputConfiguration input, GameSettings settings)
    {
        input.GamepadDeadzones(InputConfiguration.DefaultStickDeadzone, InputConfiguration.DefaultTriggerDeadzone);

        ActionBindings bindings = input.Bindings;

        // Axis contributions accumulate, so each pair adds another way to push the same axis.
        bindings.BindAxis(Move, Key.A, Key.D);
        bindings.BindAxis(Move, PadButton.DPadLeft, PadButton.DPadRight);
        bindings.BindAxis(Move, PadAxis.LeftStickX);

        bindings.Bind(Jump, settings.Input.Jump.Key, settings.Input.Jump.Pad);
    }
}
```

The shell runs `GameBoot.Start` once per run through `WithRunStart`, after saves are restored and
before the first scene. It reads the settings, hands them to `Configure`, and levels the audio.

Constructing an `InputAction` or an `AxisAction` resolves its name to a dense index, and a binding
lookup is an array read that allocates nothing. Declare each action once as a static field. Building
one per step interns a name per step. Two actions of the same name are one action.

## Read them in a step

```csharp
protected override void OnStep(in StepContext context)
{
    _velocity.X = context.Input.Axis(GameInput.Move) * _tuning.WalkSpeed;

    if (_body.IsOnFloor && context.Input.WasPressed(GameInput.Jump))
    {
        _velocity.Y = -_tuning.JumpSpeed;
    }
}
```

`IsHeld` is the state this step, `WasPressed` and `WasReleased` are the edges into it, and `Axis`
reads a value in [-1, 1] from buttons and pad axes plus any unbounded wheel notches bound to it. An
unbound action is not down and reads zero. `InputState` also carries `Pointer` and `PointerDelta`
in canvas pixels and `Scroll` in wheel notches.

One keyboard, one mouse and one gamepad are sampled. There is no device index and no second pad.

`ActiveDevice` is the device the player last used: the pad on a step a pad button goes down or a
stick leaves centre, the keyboard and mouse on a step a key or mouse button goes down, the wheel
turns or the pointer moves more than two canvas pixels. It is seeded from a pad found at boot and
is what a button prompt reads, on the step `ActiveDeviceChanged` is true.

## Gamepad deadzones and the overlay key

`InputConfiguration.GamepadDeadzones(stick, trigger)` filters the sampled pad. A run played by a
driver takes its snapshots as already filtered. `InputConfiguration.DebugMenu(button)` moves the
button that opens the development overlay, and `InputButton.None` removes it
([`debugging.md`](debugging.md)).

## Rebind at a settings screen

`Run.Input` is the configuration the shell installed, and it stays live. A settings screen replaces
what an action is bound to, and the next read sees it:

```csharp
if (context.Input.WasAnyPressed(out InputButton button))
{
    Run.Input.Bindings.Rebind(GameInput.Jump, button);
}
```

`WasAnyPressed` reports the first key, mouse button, pad button or stick direction that went down this
step, which is what a "press a button" prompt waits for. Set `FocusNavigator.Interactable` false while
it waits, so the menu behind it holds still. `InputButton.Name` is the bare name a caption shows, and
`InputButton.Device` says which slot a captured button belongs in.

Persisting a rebinding is the game's job: keep the buttons the player may change in the settings
document, where an `InputButton` field saves as `"Key.Space"`, and re-apply them at boot. A headless
run restores that document only under a named save storage; without one it plays the defaults.

## Rumble

`Run.Rumble` is the run's gamepad rumble, shaped like `Run.Audio`: a step plays pulses on it, and the
host writes the mixed level to the pad after the step.

```csharp
Run.Rumble.Play(low: 0.5f, high: 0.15f, seconds: 0.12f);
Run.Rumble.Volume = settings.RumbleStrength;
```

The composed `RumblePulse`, `Hold` and `Set`, and the mixing rule are the XML reference. The host
rests the motors on focus loss, disconnect, exit and crash, and while the keyboard or mouse is the
active device. A headless run rumbles nothing, and a driven run steps identically with or without a
pad.

## Play a run with no one at the keyboard

A run is determined by its initial state, its fixed step and the sequence of `DeviceSnapshot` values
the simulation sees. An input driver supplies that sequence, so a run plays with no window, no
graphics device and no player, the same way every time.

An `InputScript` is a fixed sequence of edits and waits, everything it measures counted in fixed
steps:

```csharp
public sealed class Walkthrough : IInputDriver
{
    private readonly IInputDriver _script = Script();

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot) => _script.TryNext(scene, tick, out snapshot);

    private static IInputDriver Script()
    {
        InputScript script = new();

        script.Down(Key.D).Wait(96).Up(Key.D);
        script.Tap(Key.Space).Wait(60);
        script.Tap(Key.Escape);

        return script.Build();
    }
}
```

A driver that has to decide what to press from what is on screen implements `IInputDriver` directly
and reads the `Scene` it is handed:

```csharp
public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
{
    if (scene is Room)
    {
        snapshot = DeviceSnapshot.Of(Key.Escape);

        return true;
    }

    snapshot = tick == 0 ? DeviceSnapshot.Of(Key.Enter) : DeviceSnapshot.Empty;

    return tick < Budget;
}
```

Returning false ends the run. Put drivers in a directory carrying a `.capsuleignore`, so they are
part of every build and of no publish
([`build-and-publish.md`](build-and-publish.md#development-only-directories)).

## The standard command line

The build registers every driver with a public parameterless constructor under its class name,
wherever the game declares it. One that takes constructor arguments registers under no name and
reaches a run through `EngineBuilder.WithInputDriver` or `CapsuleEngine.RunHeadless`.

`WithCommandLine(args)` gives the shell the engine's standard command line, so a game writes no
parser for it:

```text
dotnet run --project src/MyGame.Shell -- --scene Room --driver Walkthrough --headless
```

`--driver`, `--headless`, `--scene`, `--frames`, `--saves`, `--help` are the flags, as `--help`
prints and `EngineBuilder.WithCommandLine` documents. A shipping build keeps `--saves` and `--help`.
A refused flag and `--help` both throw `CommandLineException`, which the shell catches around its
configuration chain and reports as the process's exit code.

From a test, the same driver plays under `SimulationHost.Play` or `CapsuleEngine.RunHeadless`
([`testing.md`](testing.md)). A headless run has no overlay, no surface and no saves directory unless
one is named. A windowed driven run opens the overlay as any other run does, and stepping asks the
driver for the step's snapshot.
