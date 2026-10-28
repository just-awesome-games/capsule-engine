# Debugging

After this page you can open the debug overlay, hold and step a run, record, replay and trace it, capture its frames, and add
your own rows and geometry to it. Each type's XML docs hold its rules.

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
| Scene | `S` | Opens the scene page. It lists the scene's panel, a Camera row that opens the camera's panel, and the entities as a tree, each by its `Name`, which the scene numbers in the order entities join: `Avatar`, then `Avatar (1)`. An entity row opens that entity's panel, which lists its parent and then its children, each of which opens its own. |
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
keeps the newest 20. A recording's file is named for the scene the run was in when it was saved, by its
key, and the local time. A crash's recording path is logged, and a crash while the first scene starts saves
the boot state alone.

`--replay <file>` replays a recording in the window. It fast-forwards silently, then holds at its last
tick with the overlay open, and the status line says `Replay matched` or `Replay diverged by tick <n>`. A
run that ended by exiting holds before its exiting step. Closing the overlay, or taking a step from the
hold, hands the run to the devices from that tick, and recording carries on with the replayed run in it.
A replay never handed over saves nothing at exit. Under `--frames` the replay plays at the run's pace.
A fast-forward mixes nothing, and the sounds it started wait unplayed at the hold. Measure a run's memory
at the hold of a replay under `--frames`, where only the sounds playing at its last tick wait. With `--headless` it plays to its end and exits 0 when it matched and 1 when it diverged. In a test,
`EngineBuilder.RunHeadless(Recording)` replays one and reports `DivergedAt`.

A recording holds a checksum of the run's state once a simulated second. A replay diverges at the first
checkpoint that disagrees, at a host act whose target is gone, or where its exit disagrees with the
recording's, and plays on to its end unless the game exits first. A divergence means the simulation read
something the recording does not hold, or the game changed since it was recorded. The log names both
builds when they differ. The shell's canvas, render resolution and fixed step must match the recording's,
and a replay is refused naming the one that differs.

## Tracing a run

`--trace <csv> <ticks> <names>` beside `--headless` writes what the named entities' panels show at the
named ticks of the run a recording or a driver plays, with no overlay opened:

```text
--replay run.recording --headless --trace avatar.csv 1180..1200 Avatar
--driver Walkthrough --headless --trace avatar.csv 600.. Scene,Camera,Avatar
```

The ticks are one tick, a range such as `100..160`, an open range such as `600..` that runs to the end, or a
comma list of them. A tick is the one the overlay's tick line shows, the state after that many steps, and tick 0
is the opening scene before its first step. A log line stamped 120 was written by the step whose result tick
121 shows. The names are a comma list, and each matches an entity's `Name`, such as `Avatar (1)`, or its type name, which matches every entity of that
type. `Scene` and `Camera` trace the scene's own panel and its camera's, and `*` matches all of them.

Each CSV row is `tick,entity,section,field,value`: one field or toggle the entity's panel shows, under the
section heading it sits in. The entity is its `Name`, and a repeated section or field is numbered as a name
is. A field naming another entity holds that entity's name. A command writes no row.

A stretch is a run of consecutive ticks in one scene. It opens with a row naming the scene by its key, as
`--scene` takes it, `tick,,,Scene,<key>`, and its first tick writes every row. Each tick after it writes a row only where a
value changed. A field's rows in a stretch are its history, and its value at a tick is its last row
at or before that tick. A field gone from a panel writes an empty value. An entity gone from the trace
writes one row with an empty section and field, which ends all of its fields. An overlay act a recording
holds writes a row at the tick its result shows, `tick,<subject>,,Act,<act>`, such as a move under the moved
entity's name.

The run plays to its end and exits as one without a trace does, unless it passes the trace's
[ceiling](#ceilings). The log warns of a name that matched
nothing and of a tick past the run's end, the start of an open range included. A run that exits ends on the tick before its exiting step, where
a windowed replay holds. A trace of a recording shows the run the current build plays from its input. After
a divergence, that is no longer the run that was recorded.

## Capturing frames

`--capture <dir> <ticks>` beside `--headless` saves the frame of each named tick of the run a recording or a
driver plays, as `<dir>/<tick>.png` with the tick padded to six digits, such as `001180.png`:

```text
--replay run.recording --headless --capture frames 1180..1200
--driver Walkthrough --headless --capture frames 600,900 --trace avatar.csv 600..900 Avatar
```

The ticks are written as `--trace` takes them, and a run can do both. A tick's frame shows the state its
trace rows hold, uninterpolated. It is the surface `Run.CaptureFrame` saves: the canvas at the render
resolution, or with none the window pass. It is laid out at the output extent the run's steps saw, which
a recording holds, else at the shell's window size. It never shows the overlay or a debug draw.

The run draws on a GPU device with no window, through the driver `--gpu-driver` names, and only at the named
ticks. A device that will not open, or a directory that cannot be written, is refused before the run. A
headless run writes nothing `Run.CaptureFrame` requests, under `--capture` too. The log names the device
and the frames written, and warns of a tick past the run's end.

## Ceilings

A trace writes at most 3,600 ticks and a capture at most 600, a simulated minute and ten seconds at 60 steps
a second. A selection the run can count is refused before the run: a closed one, or an open one over a
recording, which knows its end. An open range over a driver halts the run at its first tick past the
ceiling. The run keeps what it wrote and is refused as a malformed command line is, with exit code 2.
`--force` lifts both ceilings.

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
