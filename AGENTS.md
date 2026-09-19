# Agent rules

The rules no compiler enforces. Module reference direction, the logic boundary, banned APIs,
warnings as errors, public XML documentation and packaging are gated by the build, so none of them
is repeated here. [`CONTRIBUTING.md`](CONTRIBUTING.md) is the gate every change runs.

## Scope

- A feature is admitted by a consuming game's need or by what any developer expects of a 2D engine. It ships complete, for developers beyond JAG, or it is absent.
- Unity, Godot and Unreal are the prior. A new public member reads fluently to a developer arriving from one of them: the common case is one call taking the plain thing, the composed case its own type. The precedent and the complaint it answers go in the design ledger, not here.
- Peak performance is the bar at authoring time. No heap allocation in hot paths or per-frame loops, value types for frequently created data, pooling for what the runtime spawns.
- Authoring formats are plain data that people and agents write by hand. A new format needs no tool.
- [`samples/MinimalGame/`](samples/MinimalGame/) is the review surface. An engine change lands with the sample call site that lets it be played, and migrates any break it causes there. The sample stays a small game, not a feature gallery.
- A run is driven by an input driver, not by asking a person to play it. The standard command line is the engine's: a game opts in with `WithCommandLine(args)` and re-implements no flag.
- The development overlay is `src/Capsule.Runtime/DevTools/` plus the guarded block in `CapsuleGame`. Deleting both leaves the engine compiling and every other test passing, so a host seam the overlay needs is generic host machinery. Parsers for other editors' formats are external modules that feed the build, and none lives here.
- MSBuild wildcards fold case on every platform. Separate two directories differing only by case with `DefaultItemExcludes` and ordinal `%(FullPath)` comparisons, not `Include`, `Remove` or `Exclude`.

## Public surface

- A member is public iff a game calls it or any plausible 2D game must. Test-only and cross-assembly engine members are internal, reached through `InternalsVisibleTo`.
- Generated code is game code: a member only a generator calls stays public and carries `[EditorBrowsable(Never)]`.
- Engine-owned state has no public setter. Generated roots are named for the engine: `CapsuleBoot`, `CapsuleScenes`, `CapsuleEntities`, `CapsuleAssets`.
- Before 1.0 a breaking public change takes a minor release and migrates the known consumers in the same wave.

## Docs

- XML documentation is the API reference. A consumer holding the package and its XML does not read engine source, and a source read to learn a contract is fixed with a bug's priority.
- A summary is one sentence, with a second only for a unit, ownership or lifecycle fact the signature cannot carry. An `<example>` block showing the call site is welcome where it teaches faster than prose.
- Argument validation follows .NET conventions: null, non-finite and out-of-range arguments throw the `ArgumentException` family and are not documented per member. An `<exception>` tag is for a state rule a caller can violate. Every throw's message names the defect and the fix.
- Markdown holds what spans many types, one idea once: the task pages under [`docs/`](docs/). [`PACKAGE.md`](PACKAGE.md) is every package's README, and no module carries a second one.
- A comment states an invariant or a why. Delete one that restates the line below it or addresses a reviewer.

## Tests

- Test contracts, invariants, boundaries and failure modes. A behaviour change ships with the test that would have caught its absence, a fix with the test that would have caught the bug.
- No test of an obvious implementation step, no coverage target, and no game's content or tuning in the engine suite.
