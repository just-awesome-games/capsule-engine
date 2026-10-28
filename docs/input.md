# Input

After this page you can declare a game's actions, read them in a step, and play a whole run with
nobody at the keyboard. Each type's XML docs hold its rules.

A game reads actions, never keys or pad buttons. Capsule samples one keyboard, one mouse and one gamepad,
and turns them into action state once per fixed step. What each action means is the game's. The gamepad
is the connected pad last pressed, and a press shorter than a frame still reaches a step. Capsule
ships the community controller database. A `gamecontrollerdb.txt` beside the game adds or replaces
mappings, and the log names each connected joystick that no mapping makes a gamepad, with its GUID.
Each line is in SDL's mapping format, which SDL_GameControllerDB documents and whose community tools
write by pressing the pad's buttons in turn. SDL's `SDL_GAMECONTROLLERCONFIG` environment variable,
which Steam Input sets, applies over both. A game tells its own players where the file goes.

| Need | Piece |
| --- | --- |
| A thing the player can do, apart from the device | `InputAction`, `AxisAction` |
| Which buttons and axes drive an action | `InputConfiguration.Bind`, on `Run.Input` |
| An action's state in a step | `StepContext.Input`, an `InputState` |
| Gamepad rumble | `Run.Rumble` |
| The window's cursor | `Run.Cursor` |
| A run played by code | `InputScript`, or an `IInputDriver` of your own |

## Declare the actions

A game declares its actions once at its assembly root and binds them in one place:

```csharp
public static class GameInput
{
    /// <summary>Horizontal movement, in [-1, 1].</summary>
    public static readonly AxisAction Move = new("move");

    /// <summary>Opens the pause menu.</summary>
    public static readonly InputAction Pause = new("pause");

    /// <summary>Binds every action to the devices the game supports.</summary>
    public static void Configure(InputConfiguration input)
    {
        // Axis contributions accumulate. Each pair adds another way to push the same axis.
        input.Bind(Move, Key.A, Key.D);
        input.Bind(Move, PadButton.DPadLeft, PadButton.DPadRight);
        input.Bind(Move, PadAxis.LeftStickX);

        input.Bind(Pause, Key.Escape, PadButton.Start);
    }
}
```

The function passed to `EngineBuilder.WithRunStart` hands `Run.Input` to `Configure`. Pad deadzones are
tuned in the same place with `InputConfiguration.GamepadDeadzones`.

## Read them in a step

```csharp
protected override void OnStep(in StepContext context)
{
    Position += new Vector2(context.Input.Axis(GameInput.Move) * Speed * context.DeltaSeconds, 0f);

    if (context.Input.WasPressed(GameInput.Pause) || context.Input.WindowFocusLost)
    {
        _pauseMenu.Open();
    }
}
```

Pressed state exists only inside a step, on `StepContext.Input`, and `Run.Input` holds the bindings.
`InputState` holds the pointer in canvas pixels, and `Camera.CanvasToWorld` maps it into the world.

## Show a button prompt

A prompt shows the button `Run.Input.ButtonsFor` binds on `InputState.ActiveDevice`, the device the
player last used. `InputState.PadFamily` picks the art set, and `InputState.KeyCharacter` is the
character the player's keyboard layout prints on a key. Every word a prompt shows comes from the game's
string tables ([`localization.md`](localization.md)). The pad family is recorded input like window
focus, and a driver sets it with `DeviceSnapshot.WithPadFamily`.

## Rebind at a settings screen

`Run.Input` is the configuration the shell installed, and it stays live. A settings screen replaces
what an action is bound to, and the next read sees it:

```csharp
if (context.Input.WasAnyPressed(out InputButton button))
{
    Run.Input.Rebind(GameInput.Pause, button);
}
```

A "press a button" prompt waits on `WasAnyPressed`. Set `FocusNavigator.Interactable` false while it
waits, and the menu behind it holds still.

The game persists a rebinding. It keeps the buttons the player may change in the settings document,
where an `InputButton` field saves as `"Key.Space"`, and re-applies them at run start
([`persistence.md`](persistence.md)).

## Rumble

`Run.Rumble` is the run's gamepad rumble, shaped like `Run.Audio`. A step plays pulses on it, and the
host writes the mixed level to the pad after the step.

```csharp
Run.Rumble.Play(low: 0.5f, high: 0.15f, seconds: 0.12f);
Run.Rumble.Volume = settings.RumbleStrength;
```

## The cursor

`Run.Cursor` is the pointer the window shows. A step sets it and the host applies it after the step.
Hide it, confine it, or draw a sprite in its place with the pivot as the hotspot.

```csharp
Run.Cursor.Visible = false;
Run.Cursor.Image = new Sprite(CapsuleAssets.Textures.CrosshairTexture, new TextureRegion(0, 0, 9, 9), new Vector2(4f, 4f));
```

## Play a run with no one at the keyboard

An `InputScript` is a fixed sequence of edits and waits, counted in fixed steps. A driver class wraps
one, and the shell reaches it by its class name:

```csharp
public sealed class MenuTour : IInputDriver
{
    private readonly IInputDriver _script = new InputScript()
        .Down(Key.D).Wait(60).Up(Key.D)
        .Tap(Key.Escape).Wait(30)
        .Build();

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot) => _script.TryNext(scene, tick, out snapshot);
}
```

A driver that decides what to press from what is on screen implements `IInputDriver` directly and
reads the `Scene` it is handed. The sample's
[`Walkthrough`](../samples/MinimalGame/src/MinimalGame.Game/Drivers/Walkthrough.cs) drives a whole
level. A driver reads the scene and survives tuning changes, while a recording replays one exact run
([`debugging.md`](debugging.md#record-and-replay)).

Put drivers in a directory carrying a `.capsuleignore`. They are then part of every ordinary build and
of no publish ([`build-and-publish.md`](build-and-publish.md#development-only-directories)).

## The standard command line

`WithCommandLine(args)` gives the shell the engine's standard command line, and a game writes no
parser of its own:

```text
dotnet run --project src/MyGame.Shell -- --scene PlayField --driver MenuTour --headless
```

`EngineBuilder.WithCommandLine` documents the flags and the ones a shipping build keeps. The shell reports
`--help` and a refused flag through `CommandLineException`, as
[`getting-started.md`](getting-started.md#the-shell) shows. A driver that takes constructor arguments
reaches a run through `EngineBuilder.WithInputDriver` or `EngineBuilder.RunHeadless`.

`--payload <Name>` boots the opening scene with a new instance of that `IScenePayload` class in place of
the `RunScene` call's payload. The build registers each payload class with a public parameterless
constructor, as it registers drivers.

From a test, the same driver plays under `SimulationHost.Play` or `EngineBuilder.RunHeadless`
([`testing.md`](testing.md)).
