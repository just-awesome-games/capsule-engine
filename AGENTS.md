# Agent rules

Rules no compiler enforces; module direction, role purity, public XML documentation and packaging are gated by the build.

## Building a game

- Consuming is [`docs/consuming-capsule.md`](docs/consuming-capsule.md); [`samples/MinimalGame/`](samples/MinimalGame/) is frozen — it changes only to migrate a break an engine change caused in it, or when the Creative Director asks, never to demonstrate a feature.
- A run is driven by an input driver ([`docs/headless-play.md`](docs/headless-play.md)), never by asking a person to play it.
- The standard command line is Capsule's: a game opts in with `WithCommandLine(args)` and never re-implements a flag the engine declares.

## Scope

- An engine feature is initiated by a consuming game's need and never bounded by it: complete, peak-performance, never knowingly brute-force, and serving developers beyond JAG.
- A justified public API break migrates known consuming games in the same wave; internals are simplified without breaking existing integrations.
- No hook, option or abstraction no game has asked for; public names are game-agnostic and game policy stays in the game.
- A new public member reads fluently to a developer arriving from an established engine: one primitive plus readable state over a verb shaped like the initiating feature; the precedent goes in the design ledger, not here.

## Documentation

- XML comments are the API reference and the only prose copy of a contract: units, ownership, lifecycle, exceptions, non-obvious behaviour; never a narrated signature.
- Markdown holds only what spans many types — the model, cross-cutting invariants, sequence walk-throughs, data formats, build configuration — one idea once, cross-linked; never a contract, a list the code enumerates, a decision's why, or history.
- A shipped package's README is a charter: what it is, what is inside, where the deeper documentation lives.
- A code comment states an invariant or a why the code cannot; a comment restating the line below it is deleted, as is any addressed to a reviewer.

## Boundaries

- Parsers for authoring formats are external modules feeding `*.scene.json` and `*.sheet.json` to the build; none lives here.
- Inside `Capsule.Runtime`, operating-system, file-system, window and MonoGame-platform assumptions stay in the desktop files — `SdlPlatform.cs`, `WindowsForeground.cs`, `CapsuleGame.cs`, `CrashLog.cs`, `ConsoleLogSink.cs`, `Assets/`, `Audio/`, `Input/`, `Rendering/FrameRenderer.cs`; the boot surface (`CapsuleBoot`, `CapsuleEngine`, `EngineBuilder`) carries no desktop concept beyond an application title and texture sampling.
- The development overlay is `src/Capsule.Runtime/DevTools/` plus the one guarded block in `CapsuleGame`: deleting both must leave the engine compiling and every other test passing, so a host seam the overlay needs is generic host machinery, never overlay-shaped.
- A type's assembly follows what it depends on and its namespace and folder follow its domain ([`docs/architecture.md`](docs/architecture.md#placement)); a foundational entry point may stay at an assembly root; a new assembly exists only to make the compiler enforce a reference direction; no folder or assembly is created to thin a directory.
- MSBuild wildcards fold case on every platform: two directories differing only by case are separated with `DefaultItemExcludes` and ordinal `%(FullPath)` comparisons, never `Include`/`Remove`/`Exclude`.
- Warnings are errors; a suppression carries its reason at the site. Every commit is publishable without studio-only context.

## Public surface

- A member is public iff a game calls it or any plausible 2D game must; test-only and cross-assembly engine members are internal, the latter with a per-member `InternalsVisibleTo` reason in the csproj.
- Code the generators emit into the game is game code: a member only it calls stays public and carries `[EditorBrowsable(Never)]`.
- Unused public affordances that serve plausible engine needs stay; dead implementation details go.
- Document model types are public because games author them in tests; the parser is not. Engine-owned state never has a public setter.
- Generated roots are named for the engine — `CapsuleBoot`, `CapsuleScenes`, `CapsuleEntities`, `CapsuleAssets` — and a generated asset name mirrors its key under `Assets/`.
- Before 1.0 a breaking public change takes a minor release; compatibility is not implied across minor versions.

## Tests

- Test contracts, invariants, boundaries and failure modes; a behaviour change ships with the test that would have caught its absence, a fix with the test that would have caught the bug.
- No test of an obvious implementation step, no mechanical coverage target, and no game's content or tuning asserted in the engine suite.
