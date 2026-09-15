# Debugging

Capsule has no editor; the development plane is the debugging surface of every windowed run. A trimmed or NativeAOT publish removes it and an untrimmed publish carries it disabled ([`consuming-capsule.md`](consuming-capsule.md#development-builds)).

## The overlay

Press `` ` `` in any windowed run (`InputConfiguration.DebugMenu` rebinds or removes the toggle). Open or hidden, the overlay holds the simulation on the settled step and pauses every playing voice; closing it resumes both. Its rows, keys and legend are what it prints. The overlay draws over the presented frame, never into the game's frame or a frame capture, and its keys and wheel never reach the simulation. Its Time Scale submenu sets `Run.TimeScale`, the same property a game's settings screen sets: the host's pace for the rest of the run — more or fewer steps a wall second — leaving the run itself unchanged, the same steps at the same fixed step length, played faster or slower.

The pages it opens are built from three seams a game also writes to:

- **Debug drawing** — override `OnDebugDraw` on a scene, entity or component and call `DebugDraw`; the hook's contract and the channel model are on `DebugDraw` and `Component.OnDebugDraw`.
- **Debug panels** — override `OnDebugPanel` and write `DebugPanel` rows: fields to read, commands to run, toggles to flip. The section shape, the grouping, the one stepped tick an action runs inside, and the shipping compile-out are on `DebugPanel` and `Component.OnDebugPanel`; the engine's own scene, entity and component sections mirror their public surface and nothing more.
- **Logging** — `Capsule.Diagnostics.Log`, whose host sink `EngineBuilder.WithLogSink` documents.

Nothing reads back from any of them: a field, a debug draw or a log line changes nothing, and no code can learn what the overlay shows, draws or has switched on. An action — a command or toggle, a Step, Restart, Load Scene, Remove or Exit — is a host act that changes the run as input would, and a run that took one is not reproducible from its driver alone. A time-scale change is a host act the run is reproducible across: it moves the pace and nothing the simulation is handed.

## Development builds

`Capsule.Diagnostics.Development` is the plane's contract: the runtime switch a trimmed publish folds away, and the compile symbol a game's `[Conditional]` calls answer to. `CapsuleShipping` is the one build axis that flips it, together with the development-only directories; [`consuming-capsule.md`](consuming-capsule.md#development-builds) holds the build properties.

## Headless

An input driver reproduces a run ([`headless-play.md`](headless-play.md)); a headless run has no overlay, while a windowed driven run opens it as any other and stepping asks the driver for the step's snapshot.
