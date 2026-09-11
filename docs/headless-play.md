# Headless play

A Capsule run is determined by its initial state, its fixed step, and the sequence of
`DeviceSnapshot` values the simulation sees. That sequence comes from a class — an input driver — so
a run is played with no window, no graphics device and no person at the keyboard, and plays the same
way every time.

## Input drivers

A driver implements the one method of `Capsule.Scenes.Input.IInputDriver`, which documents its
contract; `Capsule.Scenes.Input.InputScript` builds one from a fixed sequence of edits and waits.
Everything a driver measures is counted in fixed steps, never in seconds.

A driver with a public parameterless constructor is registered by the build under its class name,
which is what `--driver` takes. One that takes constructor arguments registers under no name and
reaches a run through `WithInputDriver` or `RunHeadless`. Drivers are discovered wherever the game
declares them — the shell project, the logic project, or any logic assembly the shell references —
and live in a directory carrying a `.capsuleignore`, so they are part of every build and of no
publish. See [`consuming-capsule.md`](consuming-capsule.md#development-only-directories).

## The standard command line

Capsule owns the flags that drive input, headless play and frame timing, so a game never writes a
parser for them. `WithCommandLine(args)` hands the process arguments over.

| Flag                       | Effect                                                             |
| -------------------------- | ------------------------------------------------------------------ |
| `--driver <Name>`          | Drives the run from the input driver of that class name.           |
| `--headless`               | Runs with no window, which needs a driver.                         |
| `--scene <Name>`           | Boots the registered scene of that class name.                     |
| `--frames <csv> [seconds]` | Writes host frame timing, exiting after `seconds` when given.      |
| `--help`                   | Prints the usage block on standard output and exits.               |

Flags combine, every value is required, and repeating one is an error. A game with flags of its own
removes them before handing the rest over, since anything Capsule does not declare is rejected.
Nothing is read ambiently: a shell that does not pass `args` has no command line at all.

## Running from a test

A driver plays a test as readily as it plays a window. [`testing.md`](testing.md) covers `SceneRun`,
`RunHeadless`, and which to reach for.

## Screenshots

Game logic cannot write a file, so a screenshot is an intent `Scene.CaptureFrame` raises and the
host fulfils on its next drawn frame. A windowed run writes the PNG; a headless run has no surface
to save, so it clears the request and writes nothing.
