# Headless play

A Capsule run is determined by its initial state, its fixed step, and the sequence of `DeviceSnapshot` values the simulation sees. That sequence comes from an input driver, so a run plays with no window, no graphics device and no person at the keyboard, the same way every time.

## Writing a driver

Implement `Capsule.Input.IInputDriver`, or build one from a fixed sequence of edits and waits with `Capsule.Input.InputScript`; both document their contracts, and everything a driver measures is counted in fixed steps. Put drivers in a directory carrying a `.capsuleignore`, so they are part of every build and of no publish ([`consuming-capsule.md`](consuming-capsule.md#development-only-directories)).

## Naming it

The build registers a driver with a public parameterless constructor under its class name, wherever the game declares it — the shell, the logic project, or any logic assembly the shell references. One that takes constructor arguments registers under no name and reaches a run through `EngineBuilder.WithInputDriver` or `CapsuleEngine.RunHeadless`.

## Running it

`WithCommandLine(args)` gives the shell Capsule's standard command line — `--driver`, `--headless`, `--scene`, `--frames`, `--help`, as `--help` prints and `EngineBuilder.WithCommandLine` documents — so a game never writes a parser for them:

```text
dotnet run --project src/MyGame.Shell -- --scene Room --driver Walkthrough --headless
```

From a test, the same driver plays under `SimulationHost` or `CapsuleEngine.RunHeadless`; [`testing.md`](testing.md) says which to reach for. A screenshot is an intent `Run.CaptureFrame` raises and the host fulfils on its next drawn frame; a headless run has no surface and writes nothing.
