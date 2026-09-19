# Debugging

After this page you can open the development overlay, add your own rows and geometry to it, and know what
a shipping publish drops.

## The overlay

Press `` ` `` in any windowed run. Open or hidden, the overlay holds the simulation on the settled step and
pauses every playing voice. Closing it resumes both. It draws over the presented frame, not into the game's
frame or a frame capture, and its keys and wheel do not reach the simulation. Its Time Scale submenu sets
`Run.TimeScale`, the same property a settings screen sets: more or fewer steps a wall second, at the same
fixed step length.

Nothing reads back from the overlay. No code can learn what it shows or has switched on. A command, a
toggle, Step, Restart, Load Scene, Remove or Exit is a host act that changes the run as input would, so a
run that took one is not reproducible from its driver alone. A run is reproducible across a time-scale
change.

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
`InputConfiguration.DebugMenu(button)` moves the toggle, and `InputButton.None` removes it.

## Development builds

`Capsule.Diagnostics.Development` is the contract. It is the runtime switch a trimmed publish folds away
and the compile symbol a game's `[Conditional]` calls answer to. `CapsuleShipping` is the build axis that
flips it, together with the development-only directories
([`build-and-publish.md`](build-and-publish.md#development-builds)). A trimmed or NativeAOT publish removes
the overlay and an untrimmed publish carries it disabled.

A headless run has no overlay ([`input.md`](input.md)). A windowed run driven by an input driver opens one
as any other run does, and stepping asks the driver for the step's snapshot.
