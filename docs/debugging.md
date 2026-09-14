# Debugging

Capsule has no editor; the development overlay is the debugging surface of every windowed run.

## The overlay

Press `` ` `` in any windowed run. `InputConfiguration.DebugMenu` rebinds the toggle or, given
`InputButton.None`, removes it. Opening the overlay holds the simulation on the settled step and
withholds the menu's own keys from the game; closing it resumes. The menu:

| Row        | Key           | Does                                                                     |
| ---------- | ------------- | ------------------------------------------------------------------------ |
| Step       | `Right`       | Runs exactly one fixed step through the ordinary input path; held, repeats. |
| Restart    | `R`           | Replaces the current scene with a fresh one, in one step.                |
| Load Scene | `L`           | Lists every registered scene; `Enter` loads it and stays held.          |
| Debug Draw | `D`           | Lists every channel that has drawn this session; `Enter` toggles a row. |
| Frame Pane | `F`           | Toggles the frame pane for the rest of the run.                          |
| Inspect    | `I`           | Lists the scene's entities in order; `Enter` opens one entity's state, refreshed on every Step. |
| Hide       | `H`           | Hides the panel and keeps the hold; `H` or the toggle brings it back.  |
| Exit       | `E`           | Ends the run through `Run.RequestExit`.                                 |

`Up`/`Down` move, `Enter` or a left click activates, `Backspace`/`Left` goes back; a gamepad's
d-pad and face buttons do the same; a held direction repeats. A list longer than the panel shows
a window of itself that follows the focus, and the mouse wheel scrolls the window without moving
it. The overlay draws over the presented frame and never enters the game's frame or a frame
capture; its keys and wheel never reach the simulation, and a key that served the menu is
withheld through its release.

## Frame pane

The top-right readout stays up with the menu closed or hidden and shows the last whole second,
rewritten once a second: the frame rate, the average and worst frame time, the average host
update bracket and renderer draw submission in milliseconds, the fixed steps run per second —
the fixed rate while the simulation keeps up, less when clamped — and, as of the second's end,
garbage collections per generation and the managed heap. Once on it allocates nothing per frame,
so the collection counts are the game's own.

## Debug drawing

Override `OnDebugDraw` on a component, entity or scene and call `DebugDraw.Line`, `Rect`,
`Circle`, `Capsule`, `Polygon` or `Text` on a channel of your naming. The hook runs once a step
after the step has settled, only while the overlay is attached; draw there and change nothing.
A channel appears in the Debug Draw submenu as soon as it has drawn once, starts off, and stays
drawn with the menu closed once on. Draws are world-space at the settled step and stay for the
steps a call asks for, counted in ticks, so a held run keeps them.

The engine's own channels: `Colliders` — every collider's shape as the collision world holds
it, dimmed while disabled, and the faces of the grid cells around the camera's view;
`Origins` — a cross at every world entity's position; `Camera` — the camera's bounds while it has any.
`DebugDraw.SetColor` recolours a channel or names one of your own; a colour passed on a call wins.

Nothing reads back: no code can learn whether a channel is on or what colour it draws, so a run
with draws on is the same run as one without.

## Inspecting entities

Override `OnInspect` on an entity or component and call `inspector.Field` with a label and a
value — a string, bool, integer, float, `Vector2` or enum — for each thing worth reading. The
hook runs only while the overlay is showing that entity, never in a shipping build's runtime;
report there and change nothing. The engine writes `Position` and `ZIndex` before the entity's
own fields, then each component under its own heading with a blank row between sections, so the
panel shows what an entity is made of whether or not a component reports anything — one that
reports nothing shows `<Nothing to inspect>`: colliders report their enabled state, offset,
layer and shape, a `KinematicBody2D` its floor, wall and ceiling contacts, a `SpriteAnimator`
its frame, tick, loop and finish.

`Inspect` lists the held scene's entities in scene order by type name, the second and later of
a type suffixed by their place among it — `Enemy`, `Enemy (1)`, `Enemy (2)` — and `Enter` opens
one's panel under the same name. The list and the panel are rebuilt after each Step, so stepping
with a panel open watches the values change; an entity that leaves the scene pops its panel with
a note on the status line.

Nothing reads back: an `Inspector` has no public reader, so a run that was inspected is the same
run as one that was not.

## Development builds

A shipping publish compiles out a game's `DebugDraw` and `Inspector.Field` calls, excludes every
`.capsuleignore` directory, and turns the overlay off — a trimmed or NativeAOT publish removes it, an untrimmed
one carries it disabled. `Development` states the plane — `IsSupported` is the runtime switch, `Symbol` the
compile symbol a game gates its own development calls on — and `CapsuleShipping` is the one
build axis that flips it; [`consuming-capsule.md`](consuming-capsule.md#development-builds)
holds the build properties.

## Headless

An input driver reproduces a run ([`headless-play.md`](headless-play.md)); a headless run has no
overlay, while a windowed driven run opens it as any other and stepping asks the driver for the
step's snapshot.
