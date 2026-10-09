# Debugging

After this page you can open the debug overlay, add your own rows and geometry to it, and know what
a shipping publish drops.

## Controls

| Input | Where | Action |
|---|---|---|
| `` ` `` | Any windowed run | Opens or closes the overlay, and shows a hidden one again. |
| Up, Down, D-pad up or down | Menu | Moves the focus. Held, it repeats. |
| Enter, pad south | Menu | Activates the focused row. |
| Backspace, Left, pad east, D-pad left | Menu | Returns to the previous page. |
| Right, D-pad right | Menu | Steps the game once. Held, it repeats. |
| `S`, `D`, `T`, `L`, `F1` | Root page | Opens Scene, Debug Draw, Time Scale, Load Scene or Help. |
| `R`, `C`, `F`, `H`, `E` | Any page | Restart, Game Camera, Frame Pane, Hide or Exit. |
| `H`, `C` | Hidden | Shows the menu again, or returns the view to the game's camera. |
| Pointer, left click | Row | Pointing focuses the row and a click activates it. |
| Wheel | Menu | Scrolls the rows, with or without a modifier. |
| Wheel | World | Scrolls the view vertically. Wheel up moves the view up. |
| Shift+wheel, Alt+wheel, horizontal wheel | World | Scrolls the view horizontally. |
| Ctrl+wheel | World | Zooms about the pointer, from four times closer to eight times further than the game's camera. |
| Middle-drag, Space+left-drag | World | Pans the view. |
| `G` | Entity panel, open or hidden | Moves the entity to the world point last under the pointer, as one step. A camera following it or anything under it cuts with it. |

`InputConfiguration.DebugOverlay(button)` moves the toggle, and `InputButton.None` removes it. World
inputs work open or hidden, over the drawn world clear of the menu. A wheel notch scrolls a
tenth of the view's visible span.

## Overlay actions

| Row | Key | Action |
|---|---|---|
| Scene | `S` | Opens the scene page. It lists the scene's panel, a Camera row that opens the camera's panel, and the entities as a tree. An entity row opens that entity's panel. |
| Step | Right | Runs one fixed step and holds again. |
| Debug Draw | `D` | Lists the channels that have emitted and switches each on or off. |
| Time Scale | `T` | Sets `Run.TimeScale` from 0.25x to 4x. |
| Restart | `R` | Restarts the scene. |
| Load Scene | `L` | Loads a registered scene. The row shows only when scenes are registered. |
| Game Camera | `C` | Returns the view to the game's camera. |
| Frame Pane | `F` | Shows or hides the frame rate, frame times, steps per second, GC counts and heap. |
| Hide | `H` | Withdraws the menu and keeps the run held. |
| Help | `F1` | Lists each control no row shows by its main keys. |
| Exit | `E` | Ends the run. |

## Holding the run

Open or hidden, the overlay holds the simulation on the settled step, pauses every playing voice and
rests the pad's motors. Closing it resumes all three. Its menu and panes draw over the presented frame.
Its keys, its mouse buttons, Ctrl, Shift, Alt and the wheel do not reach the simulation. A press that
served the overlay stays withheld from the game until it is released. A frame capture never shows the
overlay or its camera.

The first pan, scroll or zoom draws the game's frame through the overlay's own camera. Panning, scrolling
and zooming never write the game's `Camera`. `C`, the Game Camera row, a scene change and closing the
overlay return the view to the game's camera. A frame capture requested meanwhile waits for the game's
camera. Zoomed out, a declared render surface is drawn at a whole multiple of its resolution, up to the
next whole scale it is presented at. The menu's top three lines show the scene, the tick and the world
point last under the pointer.

Nothing reads back from the overlay. No code can learn what it shows or has switched on. A command, a
toggle, Step, Move, Restart, Load Scene, Remove or Exit is a host act that changes the run as input would. A run
that took one is not reproducible from its driver alone. A time-scale change leaves a run reproducible.

## The three seams a game writes to

| Seam | What a game writes | Contract |
|---|---|---|
| Debug drawing | Override `OnDebugDraw` on a scene, entity or component and call `DebugDraw`. | `DebugDraw` and `Component.OnDebugDraw` |
| Debug panels | Override `OnDebugPanel` on a scene, entity, component or camera and write rows: fields to read, commands to run, toggles to flip. | `DebugPanel` |
| Logging | Call `Capsule.Diagnostics.Log`. A headless test installs `CollectingLogSink`. | `EngineBuilder.WithLogSink` |

```csharp
protected override void OnDebugPanel(DebugPanel panel)
{
    panel.Field("Current Health", Health);
    panel.Command("Heal", () => Health++);
}
```

A debug draw, a panel field or a log line changes nothing about the run.

## Development builds

The overlay is development code. A publish drops it or carries it disabled, as
[`build-and-publish.md`](build-and-publish.md#development-builds) describes, and a game gates its own tools
on `Capsule.Diagnostics.Development`.

A headless run has no overlay. A windowed run driven by an input driver opens one as any other run does,
and stepping asks the driver for the step's snapshot.
