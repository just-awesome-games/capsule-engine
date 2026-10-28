# Debugging

After this page you can open the debug overlay, hold and step a run, record and replay it, and add your
own rows and geometry to it. Each type's XML docs hold its rules.

The debug overlay is development code. It holds the run while it is open, and a game writes to it
through three seams and reads nothing back.

## Controls

| Input | Where | Action |
|---|---|---|
| `` ` `` | Any windowed run | Opens or closes the overlay, and shows a hidden one again. |
| Up, Down, D-pad up or down | Menu | Moves the focus. Held, it repeats. |
| Enter, pad south | Menu | Activates the focused row. |
| Backspace, Left, pad east, D-pad left | Menu | Returns to the previous page. |
| Right, D-pad right | Menu | Steps the game once. Held, it repeats. |
| `S`, `D`, `T`, `L`, `F1` | Root page | Opens Scene, Debug Draw, Time Scale, Load Scene or Help. |
| `R`, `C`, `P`, `W`, `H`, `E` | Any page | Restart, Game Camera, Frame Pane, Save Recording, Hide or Exit. |
| `H`, `C` | Hidden | Shows the menu again, or returns the view to the game's camera. |
| Pointer, left click | Row | Pointing focuses the row and a click activates it. |
| Wheel | Menu | Scrolls the rows, with or without a modifier. |
| Wheel | World | Scrolls the view vertically. Wheel up moves the view up. |
| Shift+wheel, Alt+wheel, horizontal wheel | World | Scrolls the view horizontally. |
| Ctrl+wheel | World | Zooms about the pointer, from four times closer to eight times further than the game's camera. |
| Middle-drag, Space+left-drag | World | Pans the view. |
| Left click | World, open or hidden | Selects the entity drawn under the pointer, outermost first, and opens its panel. Another click on the same spot selects the next one down. |
| `G` | Entity panel, open or hidden | Moves the entity to the world point last under the pointer, as one step. A camera following it or anything under it cuts with it. |
| `F`, pad north | Entity panel, open or hidden | Flies the view to the entity and everything under it. |

`InputConfiguration.DebugOverlay(button)` moves the toggle, and `InputButton.None` removes it. World
inputs work open or hidden, over the drawn world clear of the menu. A wheel notch scrolls a
tenth of the view's visible span.

## Overlay actions

| Row | Key | Action |
|---|---|---|
| Scene | `S` | Opens the scene page. It lists the scene's panel, a Camera row that opens the camera's panel, and the entities as a tree. An entity row opens that entity's panel, which lists its parent and then its children, each of which opens its own. |
| Step | Right | Runs one fixed step and holds again. |
| Debug Draw | `D` | Lists the channels that have emitted and switches each on or off. |
| Time Scale | `T` | Sets `Run.TimeScale` from 0.25x to 4x. |
| Restart | `R` | Restarts the scene. |
| Load Scene | `L` | Loads a registered scene. The row shows only when scenes are registered. |
| Game Camera | `C` | Returns the view to the game's camera. |
| Frame Pane | `P` | Shows or hides the frame rate, frame times, steps per second, GC counts and heap. With sound, it also counts `underruns`, when the mix fell behind the device, and `starved` blocks, when a streamed voice's decoding fell behind. |
| Save Recording | `W` | Saves the run so far to `Recordings/`, and the status line names the file. The row shows only while the run records. |
| Hide | `H` | Withdraws the menu and keeps the run held. |
| Help | `F1` | Lists each control no row shows by its main keys. |
| Exit | `E` | Ends the run. |

## Holding the run

Open or hidden, the overlay holds the simulation on the settled step, pauses every playing voice and
rests the pad's motors. Closing it resumes all three. Its menu and panes draw over the presented frame.
Its keys, its mouse buttons, Ctrl, Shift, Alt and the wheel do not reach the simulation. A press that
served the overlay stays withheld from the game until it is released. A frame capture never shows the
overlay or its camera.

Panning, scrolling and zooming move the overlay's own camera and never the game's `Camera`. `C`, a scene
change and closing the overlay return the view to the game's camera. The menu's top lines show the scene,
the tick, and the world point under the pointer with what is drawn there.

Selecting an entity outlines its renderers and draws every debug draw it and what is under it made,
whatever the Debug Draw page has switched on. `F` flies the view to it.

Nothing reads back from the overlay. No code can learn what it shows or has switched on. A command, a
toggle, Step, Move, Restart, Load Scene or Exit is a host act that changes the run as input would. A run
that took one is not reproducible from its driver alone, and its recording holds the act and reproduces
it. A time-scale change leaves a run reproducible.

A headless run has no overlay. A windowed run driven by an input driver opens one as any other run does,
and stepping asks the driver for the step's snapshot.

## Record and replay

Every windowed development run records itself from boot: its seed, opening scene, restored saves, each
step's input, and each host act at its tick. `EngineBuilder.WithoutRecording` turns it off, and a
headless run never records. Recordings live in `Recordings/` in the game's per-user local folder, beside
`crash.log`, or under the directory `--recordings` names. Save Recording writes there, and the engine
never deletes those files. A normal exit and a crash each save the run to `Recordings/Recent/`, which
keeps the newest 20. A crash's recording path is logged, and a crash while the first scene starts saves
the boot state alone.

`--replay <file>` replays a recording in the window. It fast-forwards silently, then holds at its last
tick with the overlay open, and the status line says `Replay matched` or `Replay diverged by tick <n>`. A
run that ended by exiting holds before its exiting step. Closing the overlay, or taking a step from the
hold, hands the run to the devices from that tick, and recording carries on with the replayed run in it.
A replay never handed over saves nothing at exit. Under `--frames` the replay plays at the run's pace.
With `--headless` it plays to its end and exits 0 when it matched and 1 when it diverged. In a test,
`EngineBuilder.RunHeadless(Recording)` replays one and reports `DivergedAt`.

A recording holds a checksum of the run's state once a simulated second. A replay diverges at the first
checkpoint that disagrees, at a host act whose target is gone, or where its exit disagrees with the
recording's, and plays on to its end unless the game exits first. A divergence means the simulation read
something the recording does not hold, or the game changed since it was recorded. The log names both
builds when they differ. The shell's canvas, render resolution and fixed step must match the recording's,
and a replay is refused naming the one that differs.

## The three seams a game writes to

| Seam | What a game writes | Contract |
|---|---|---|
| Debug drawing | Override `OnDebugDraw` on a scene, entity or component and call `DebugDraw`. | `DebugDraw` and `Component.OnDebugDraw` |
| Debug panels | Override `OnDebugPanel` on a scene, entity, component or camera and write rows: fields to read, commands to run, toggles to flip. | `DebugPanel` |
| Logging | Call `Capsule.Diagnostics.Log`. A headless test installs `CollectingLogSink`. | `EngineBuilder.WithLogSink` |

Every debug-draw channel starts switched off. Open the overlay and switch the channel on under Debug Draw,
and its draws then stay on screen with the overlay closed.

```csharp
protected override void OnDebugDraw()
{
    DebugDraw.Line("Aim", WorldPosition, WorldPosition + (_facing * 24f));
    DebugDraw.Rect("Aim", new Rect(WorldPosition - new Vector2(8f), new Vector2(16f)));
}

protected override void OnDebugPanel(DebugPanel panel)
{
    panel.Field("Count", _count);
    panel.Command("Reset", () => _count = 0);
}
```

A debug draw, a panel field or a log line changes nothing about the run. What a shipping publish drops is
in [`build-and-publish.md`](build-and-publish.md#development-builds).
