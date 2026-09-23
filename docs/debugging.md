# Debugging

After this page you can open the development overlay, add your own rows and geometry to it, and know what
a shipping publish drops.

## The overlay

Press `` ` `` in any windowed run. Open or hidden, the overlay holds the simulation on the settled step and
pauses every playing voice. Closing it resumes both. It draws over the presented frame, not into the game's
frame or a frame capture, and its keys and wheel do not reach the simulation. Its Time Scale submenu sets
`Run.TimeScale`.

Nothing reads back from the overlay. No code can learn what it shows or has switched on. A command, a
toggle, Step, Restart, Load Scene, Remove or Exit is a host act that changes the run as input would. A run
that took one is not reproducible from its driver alone. A time-scale change leaves a run reproducible.

## The three seams a game writes to

- **Debug drawing.** Override `OnDebugDraw` on a scene, entity or component and call `DebugDraw`. The
  hook's contract and the channel model are on `DebugDraw` and `Component.OnDebugDraw`.
- **Debug panels.** Override `OnDebugPanel` and write `DebugPanel` rows: fields to read, commands to run,
  toggles to flip.

  ```csharp
  protected override void OnDebugPanel(DebugPanel panel)
  {
      panel.Field("Current Health", Health);
      panel.Command("Heal", () => Health++);
  }
  ```

- **Logging.** `Capsule.Diagnostics.Log`, whose host sink `EngineBuilder.WithLogSink` documents. A headless
  test installs `CollectingLogSink`.

A debug draw, a panel field or a log line changes nothing about the run.
`InputConfiguration.DebugMenu(button)` moves the key that opens the overlay, and `InputButton.None`
removes it.

## Development builds

The overlay is development code. A publish drops it or carries it disabled, as
[`build-and-publish.md`](build-and-publish.md#development-builds) describes, and a game gates its own tools
on `Capsule.Diagnostics.Development`.

A headless run has no overlay. A windowed run driven by an input driver opens one as any other run does,
and stepping asks the driver for the step's snapshot.
