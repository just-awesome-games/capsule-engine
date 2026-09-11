# Agent rules

These rules cover judgments the build cannot enforce. Read diagnostics before adding prose: architecture, role legality, public API documentation, and packaging are already gated.

## Building a game

Consuming Capsule is documented in [`docs/consuming-capsule.md`](docs/consuming-capsule.md); the runnable minimal game is [`samples/MinimalGame/`](samples/MinimalGame/), which is frozen: it changes only to migrate a break an engine change caused in it, or when the Creative Director asks for it, and never to demonstrate a new engine feature.

A run is driven by an input driver, never by asking a person to play it: see [`docs/headless-play.md`](docs/headless-play.md) for writing one, naming it on the command line, and running it headlessly.

The standard command line is Capsule's: a game opts in with `WithCommandLine(args)` and never re-implements a flag the engine already declares.

## Scope

Engine features are initiated by a consuming game's need, never bounded by it: what lands must meet the bar of a high-class open-source engine — peak performance, a modern feature-set, no knowingly suboptimal or brute-force implementations, no half-built features. That bar is not a compatibility ceremony: JAG's own games are the only considered consumers, so break a public API whenever the better design needs it and migrate the consuming game in the same wave. Do not add hooks, options, or abstractions no game has asked for. Keep public names game-agnostic, and leave game policy in the game.

A new public member reads fluently to a developer arriving from an established engine: name the Unity or Godot precedent in the ledger entry when one exists, and prefer one primitive plus readable state over a verb shaped like the initiating game's feature.

## Documentation

XML comments are the API reference and the only prose copy of public behavior. Keep them precise about units, ownership, lifecycle, exceptions, and non-obvious contracts; do not narrate signatures.

Markdown is limited to onboarding, cross-cutting architecture, build configuration, data formats, and standard project contracts. Keep all documentation declarative and current: no changelogs, migration notes, decision history, or forward references to unimplemented features.

Every shipped package carries a charter README: what it is, what is inside it, and where the deeper documentation lives — never API reference, and never substance that belongs to `docs/`.

Comments explain invariants and hazards the code cannot state. Delete walkthroughs, section labels, and commentary addressed to reviewers.

## Boundaries

The build enforces module direction and game-role purity. Two boundaries remain review-owned. Parsers for authoring formats do not live in this repository; they are external modules feeding `*.scene.json` to the build. Inside `Capsule.Runtime`, operating-system, file-system, window and MonoGame-platform assumptions stay in the desktop files — `SdlPlatform.cs`, `WindowsForeground.cs`, `CapsuleGame.cs`, `CrashLog.cs`, `ConsoleLogSink.cs`, `Assets/`, `Audio/`, `Input/` and `Rendering/FrameRenderer.cs`; the neutral hosting files make none of their own, and the boot surface a game's shell is generated against — `CapsuleBoot`, `CapsuleEngine` and `EngineBuilder` — carries no desktop concept beyond an application title and texture sampling.

Choose the existing assembly by dependency boundary, then group source and tests by the subsystem that owns the behavior: for example, rendering contracts belong under `Core/Rendering`, renderer components under `Scenes/Rendering`, device rendering under `Runtime/Rendering`, loading/cache/lifetime code under `Runtime/Assets`, and generator tests under `Generators`. Match namespaces to domain folders by default; organizational subfolders need not rename API types. Foundational entry points may remain at an assembly root. Do not create speculative folders or new assemblies merely to reduce a directory's file count; `docs/project-layout.md` governs game layout, not engine layout.

MSBuild wildcards fold case on every platform, Linux included, so no `Include`, `Remove`, or `Exclude` can separate two directories that differ only by case: carve them apart with `DefaultItemExcludes` and ordinal `%(FullPath)` comparisons, as the targets already do.

Warnings are fixed or suppressed with the reason at the suppression site. Every commit must remain publishable without studio-only context.

## Public surface

- A member is public iff a game calls it or any plausible 2D game must.
- Code the generators emit into the game is game code — members only it calls stay public and carry `[EditorBrowsable(Never)]`.
- Cross-assembly engine use is internal plus a per-member `InternalsVisibleTo` with its reason in the csproj.
- Test-only members are internal.
- Nothing calls it, delete it.
- Document model types are public because games author them in tests; the parser is not.
- Engine-owned state never has a public setter.
- Generated roots are named for the engine — `CapsuleBoot`, `CapsuleScenes`, `CapsuleEntities` and the one asset root `CapsuleAssets` — and a generated asset name mirrors its key under `Assets/`, which is its authored path normalized segment by segment.

## Tests

Test contracts, invariants, boundaries, and failure modes. Do not test obvious implementation steps, target coverage mechanically, or assert a game's content and tuning in the engine suite.
