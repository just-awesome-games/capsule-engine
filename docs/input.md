# Input

After this page you can declare a game's actions, read them in a step, and play a whole run with
nobody at the keyboard.

## Declare the actions

An action is a named thing the player can do, apart from the device that does it. A game
declares its actions once at its assembly root and binds them in one place. Every scene then reads
actions, never keys or pad buttons:

```csharp
public static class GameInput
{
    /// <summary>Horizontal movement, in [-1, 1].</summary>
    public static readonly AxisAction Move = new("move");

    /// <summary>Leaves the floor.</summary>
    public static readonly InputAction Jump = new("jump");

    /// <summary>Binds every action to the devices the game supports.</summary>
    public static void Configure(InputConfiguration input, GameSettings settings)
    {
        ActionBindings bindings = input.Bindings;

        // Axis contributions accumulate, so each pair adds another way to push the same axis.
        bindings.BindAxis(Move, Key.A, Key.D);
        bindings.BindAxis(Move, PadButton.DPadLeft, PadButton.DPadRight);
        bindings.BindAxis(Move, PadAxis.LeftStickX);

        bindings.Bind(Jump, settings.Input.Jump.Key, settings.Input.Jump.Pad);
    }
}
```

The sample's shell passes `GameBoot.Start` to `EngineBuilder.WithRunStart`, which runs it once after
saves are restored. It reads the settings and hands them to `Configure`. Declare each action once as a
static field, as above. Constructing one interns its name. A game that tunes the sampled pad's
deadzones calls `InputConfiguration.GamepadDeadzones` in the same place.

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

`InputState` holds each action's state for the step and the pointer in canvas pixels.
`Camera.CanvasToWorld` maps a pointer position into the world. A button prompt reads
`InputState.ActiveDevice`, the device the player last used.

Window focus reads as held state with edges, and the game decides what a loss means:

```csharp
if (context.Input.WindowFocusLost && !Paused)
{
    _pauseMenu.Open();
}
```

One keyboard, one mouse and one gamepad are sampled. There is no device index and no second pad.

## Rebind at a settings screen

`Run.Input` is the configuration the shell installed, and it stays live. A settings screen replaces
what an action is bound to, and the next read sees it:

```csharp
if (context.Input.WasAnyPressed(out InputButton button))
{
    Run.Input.Bindings.Rebind(GameInput.Jump, button);
}
```

A "press a button" prompt waits on `WasAnyPressed`. Set `FocusNavigator.Interactable` false while it
waits, and the menu behind it holds still. `InputButton.Name` is the bare name a caption shows.

The game persists a rebinding. It keeps the buttons the player may change in the settings document,
where an `InputButton` field saves as `"Key.Space"`, and re-applies them at run start
([`persistence.md`](persistence.md)).

## Rumble

`Run.Rumble` is the run's gamepad rumble, shaped like `Run.Audio`: a step plays pulses on it, and the
host writes the mixed level to the pad after the step.

```csharp
Run.Rumble.Play(low: 0.5f, high: 0.15f, seconds: 0.12f);
Run.Rumble.Volume = settings.RumbleStrength;
```

When the host rests the motors is on `Run.Rumble`. A headless run rumbles nothing.

## Play a run with no one at the keyboard

An input driver supplies the `DeviceSnapshot` sequence a run sees. Under the
[determinism contract](architecture.md#determinism-contract) a driven run plays the same way every time,
with no window, no graphics device and no player.

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

Returning false ends the run. Put drivers in a directory carrying a `.capsuleignore`. They are then
part of every ordinary build and of no publish
([`build-and-publish.md`](build-and-publish.md#development-only-directories)).

## The standard command line

The build registers every driver with a public parameterless constructor under its class name,
wherever the game declares it. One that takes constructor arguments registers under no name and
reaches a run through `EngineBuilder.WithInputDriver` or `CapsuleEngine.RunHeadless`.

`WithCommandLine(args)` gives the shell the engine's standard command line, and a game writes no
parser of its own:

```text
dotnet run --project src/MyGame.Shell -- --scene Room --driver Walkthrough --headless
```

`--driver`, `--headless`, `--scene`, `--frames`, `--saves`, `--help` are the flags, as `--help`
prints and `EngineBuilder.WithCommandLine` documents. A shipping build keeps `--saves` and `--help`.
A refused flag and `--help` both throw `CommandLineException`, which the shell catches around its
configuration chain and reports as the process's exit code.

From a test, the same driver plays under `SimulationHost.Play` or `CapsuleEngine.RunHeadless`
([`testing.md`](testing.md)). How a driven run meets the development overlay is
[`debugging.md`](debugging.md#development-builds).
