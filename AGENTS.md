# Agent rules

Rules no compiler enforces; module direction, role purity, public XML documentation and packaging are gated by the build.

## Building a game

- Consuming is [`docs/consuming-capsule.md`](docs/consuming-capsule.md). [`samples/MinimalGame/`](samples/MinimalGame/) is the shipped showcase of how a game is best built on Capsule and the engine's review surface: its call sites are the patterns developers and agents copy, so every engine change asks whether the sample should carry it, lands with the call site that lets it be playtested and its integration reviewed when it should, and migrates any break it causes there. It stays a small game, never a feature gallery, and its shape never constrains an engine change.
- A run is driven by an input driver ([`docs/headless-play.md`](docs/headless-play.md)), never by asking a person to play it.
- The standard command line is Capsule's: a game opts in with `WithCommandLine(args)` and never re-implements a flag the engine declares.

## Scope

- A feature is admitted by a consuming game's need or by what any developer expects of a 2D engine, ahead of a call site and never bounded by the initiating game: complete, serving developers beyond JAG, and absent rather than half-built.
- Peak performance is the bar at authoring time, never a later rung: the most performant shape already known is the one written, a known improvement is never deferred to a profiler, and a simplification pass reports its per-hot-path cost. No heap allocation in hot paths or per-frame loops; value types for frequently created data; pooling for what the runtime spawns; allocation-free iteration over engine collections.
- Established engines — Unity, Godot, Unreal — are the prior: know what each exposes, how it is built, and what its developers wish it did instead, then build the wish and never carry the grievance forward; a problem they solved is not re-derived. A new public member reads fluently to a developer arriving from one of them: the common case is one call taking the plain thing, the composed case its own type, never a parameter bag; one primitive plus readable state over a verb shaped like the initiating feature. The precedent and the complaint it answers go in the design ledger, not here.
- Capsule is code-first and its authoring formats are plain data — `*.scene.json`, `*.sheet.json` — written by hand by people and agents alike; a new format needs no tool to author.
- A justified public API break migrates known consuming games in the same wave; internals are simplified without breaking existing integrations.
- No hook, option or abstraction without a plausible 2D-game consumer; public names are game-agnostic and game policy stays in the game.

## Documentation

- XML comments are the API reference and the only prose copy of a contract: units, ownership, lifecycle, exceptions, non-obvious behaviour; never a narrated signature. A consumer holding only the package and its XML never needs engine source; a source read to learn a contract is a doc defect, fixed with a bug's priority.
- Markdown holds only what spans many types — the model, cross-cutting invariants, sequence walk-throughs, data formats, build configuration — one idea once, cross-linked; never a contract, a list the code enumerates, a decision's why, or history.
- Every sentence, in XML, Markdown or a comment, states a fact the code cannot; prose that narrates types, members or steps is deleted.
- [`PACKAGE.md`](PACKAGE.md) is every package's README and stays a charter: what ships, what is inside, where the deeper documentation lives. No module carries a second one.
- A code comment states an invariant or a why the code cannot; a comment restating the line below it is deleted, as is any addressed to a reviewer.

## Boundaries

- Parsers for authoring formats are external modules feeding `*.scene.json` and `*.sheet.json` to the build; none lives here.
- `Capsule.Runtime` is the platform-neutral host: it holds no implicit location and no native binding, enforced by its banned-API list, and consults `HostPlatform` for every one. Platform code lives in a platform module — `Capsule.Runtime.Desktop` is the shipped one — and the runtime grants Desktop no internals, so the module stays the proof of the public contract. The boot surface (`CapsuleBoot`, `CapsuleEngine`, `EngineBuilder`) carries no platform concept beyond the platform argument, an application title and texture sampling.
- The development overlay is `src/Capsule.Runtime/DevTools/` plus the one guarded block in `CapsuleGame`: deleting both must leave the engine compiling and every other test passing, so a host seam the overlay needs is generic host machinery, never overlay-shaped.
- A type's assembly follows what it depends on and its namespace and folder follow its domain ([`docs/architecture.md`](docs/architecture.md#placement)); a foundational entry point may stay at an assembly root; a new assembly exists only to make the compiler enforce a reference direction; no folder or assembly is created to thin a directory.
- MSBuild wildcards fold case on every platform: two directories differing only by case are separated with `DefaultItemExcludes` and ordinal `%(FullPath)` comparisons, never `Include`/`Remove`/`Exclude`.
- Warnings are errors; a suppression carries its reason at the site. Every commit is publishable without studio-only context.

## Public surface

- A member is public iff a game calls it or any plausible 2D game must; test-only and cross-assembly engine members are internal, reached through `InternalsVisibleTo`.
- Code the generators emit into the game is game code: a member only it calls stays public and carries `[EditorBrowsable(Never)]`.
- Unused public affordances that serve plausible engine needs stay; dead implementation details go.
- Document model types are public because games author them in tests; the parser is not. Engine-owned state never has a public setter.
- Generated roots are named for the engine — `CapsuleBoot`, `CapsuleScenes`, `CapsuleEntities`, `CapsuleAssets` — and a generated asset name mirrors its key under `Assets/`.
- Before 1.0 a breaking public change takes a minor release; compatibility is not implied across minor versions.

## Tests

- Test contracts, invariants, boundaries and failure modes; a behaviour change ships with the test that would have caught its absence, a fix with the test that would have caught the bug.
- No test of an obvious implementation step, no test whose failure no consumer would notice, no mechanical coverage target, and no game's content or tuning asserted in the engine suite.
