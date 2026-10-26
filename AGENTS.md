# Agent rules

The rules no gate enforces. The build and the test suite gate module reference direction, the logic
boundary, banned APIs, warnings as errors, public XML documentation, member order, test file length,
the prose marks a scan can find and packaging, and none of them is repeated here.
[`CONTRIBUTING.md`](CONTRIBUTING.md) is the gate every change runs.

## Scope

- A feature is admitted by a consuming game's need or by what any developer expects of a 2D engine. It ships complete, for developers beyond JAG, or it is absent.
- A new public member reads fluently to a developer arriving from any established engine: the common case is one call taking the plain thing, the composed case its own type.
- Peak performance is the bar at authoring time. No heap allocation in hot paths or per-frame loops, value types for frequently created data, pooling for what the runtime spawns.
- Authoring formats are plain data that people and agents write by hand. A new format needs no tool.
- [`samples/MinimalGame/`](samples/MinimalGame/) is the review surface. An engine change lands with the sample call site that lets it be played, and migrates any break it causes there. The sample stays a small game, not a feature gallery. The sample's README says what it is, how to run it and how to play it, and the code says the rest.
- A run is driven by an input driver, not by asking a person to play it. The standard command line is the engine's: a game opts in with `WithCommandLine(args)` and re-implements no flag.
- Development-only engine code, the debug overlay among it, lives in `src/Capsule.Runtime/DevTools/` or behind `Development.IsSupported`, which a shipping publish switches off and trims. A seam the overlay needs in shipping code costs a running game nothing measurable. Parsers for other editors' formats are external modules that feed the build, and none lives here.
- MSBuild wildcards fold case on every platform. Separate two directories differing only by case with `DefaultItemExcludes` and ordinal `%(FullPath)` comparisons, not `Include`, `Remove` or `Exclude`.

## Public surface

- A member is public iff a game calls it or any plausible 2D game must. Test-only and cross-assembly engine members are internal, reached through `InternalsVisibleTo`.
- Generated code is game code: a member only a generator calls stays public and carries `[EditorBrowsable(Never)]`.
- Engine-owned state has no public setter. Generated roots are named for the engine: `CapsuleBoot`, `CapsuleScenes`, `CapsuleEntities`, `CapsuleAssets`.
- Before 1.0 a breaking public change takes a minor release and migrates the known consumers in the same wave. A removed field or property is deleted outright. No code stays behind to point at its replacement.

## Code

- An asset type's folder holds its format classes, its step and its generated-member writer.

## Docs

- XML documentation is the API reference. A consumer holding the package and its XML does not read engine source, and a source read to learn a contract is fixed with a bug's priority.
- A summary is one sentence, with a second only for a unit, ownership or lifecycle fact the signature cannot carry. An `<example>` block showing the call site is welcome where it teaches faster than prose.
- A summary names a default that is not the type's zero value, and what a zero default means when zero switches the feature off.
- Argument validation follows .NET conventions: null, non-finite and out-of-range arguments throw the `ArgumentException` family and are not documented per member. An `<exception>` tag is for a state rule a caller can violate. Every throw's message names the defect and the fix.
- Markdown holds what spans many types, one idea once: the task pages under [`docs/`](docs/). [`PACKAGE.md`](PACKAGE.md) is every package's README, and no module carries a second one.
- An example in XML or on a page uses neutral names such as `Avatar` or `north`, never a genre's nouns such as enemies or coins. A page carries no genre recipe.
- A behaviour change rewrites every page, XML line and comment that states the old behaviour, in the same change.
- A comment states an invariant or a why. Delete one that restates the line below it or addresses a reviewer.
- Docs, XML and comments name no other engine. Capsule is described on its own terms. A precedent and the complaint it answers stay out of this repo. A dependency or an interchange format is named as what it is.
- Plain sentences, subject then verb, with no inversion ("Idempotent, because...").

## Tests

- Test contracts, invariants, boundaries and failure modes. A behaviour change ships with the test that would have caught its absence, a fix with the test that would have caught the bug.
- No test of an obvious implementation step, no coverage target, and no game's content or tuning in the engine suite.
- One canonical test per contract. Permutations of the same guard are one theory or none. Merging tests keeps every contract each one held, down to the input that made a row distinct.
- A fixture shape has one owner. A test reuses it rather than declaring a near copy in its own file.
