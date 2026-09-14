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
| Hide       | `H`           | Hides the panel and keeps the hold; `H` or the toggle brings it back.  |
| Exit       | `E`           | Ends the run through `Run.RequestExit`.                                 |

`Up`/`Down` move, `Enter` or a left click activates, `Backspace`/`Left` goes back; a gamepad's
d-pad and face buttons do the same. The overlay draws over the presented frame and never enters
the game's frame or a frame capture; its keys never reach the simulation, and a key that served
the menu is withheld through its release.

## Debug drawing

Override `OnDebugDraw` on a component, entity or scene and call `DebugDraw.Line`, `Rect`,
`Circle`, `Capsule`, `Polygon` or `Text` on a channel of your naming. The hook runs once a step
after the step has settled, only while the overlay is attached; draw there and change nothing.
A channel appears in the Debug Draw submenu as soon as it has drawn once, starts off, and stays
drawn with the menu closed once on. Draws are world-space at the settled step and stay for the
steps a call asks for, counted in ticks, so a held run keeps them.

The engine's own channels: `Colliders` — every collider's shape as the collision world holds
it, dimmed while disabled, and the faces of the grid cells around the camera's view;
`Origins` — a cross at every entity's position; `Camera` — the camera's bounds while it has any.
`DebugDraw.SetColor` recolours a channel or names one of your own; a colour passed on a call wins.

Nothing reads back: no code can learn whether a channel is on or what colour it draws, so a run
with draws on is the same run as one without.

## Development builds

A shipping publish compiles out a game's `DebugDraw` calls, excludes every `.capsuleignore`
directory, and turns the overlay off — a trimmed or NativeAOT publish removes it, an untrimmed
one carries it disabled. `Development` states the plane — `IsSupported` is the runtime switch, `Symbol` the
compile symbol a game gates its own development calls on — and `CapsuleShipping` is the one
build axis that flips it; [`consuming-capsule.md`](consuming-capsule.md#development-builds)
holds the build properties.

## Headless

An input driver reproduces a run ([`headless-play.md`](headless-play.md)); a headless run has no
overlay, while a windowed driven run opens it as any other and stepping asks the driver for the
step's snapshot.
