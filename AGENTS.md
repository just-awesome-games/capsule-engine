# Agents

Operating rules for anyone changing this repository. They are short because every one of them
is a rule the compiler, a test or CI cannot already enforce for you.

## Code is the documentation plane

Agents reason through code, not prose. **The whole narrative prose surface is this file,
[`README.md`](README.md), [`docs/architecture.md`](docs/architecture.md),
[`docs/consuming-capsule.md`](docs/consuming-capsule.md) and
[`Capsule.Maps/README.md`](Capsule.Maps/README.md).** Nothing else. A new prose document, or
a second module README, needs a maintainer-ratified reason — a module whose story is told by its
API gets none.

**Contract and community files sit outside that cap.** They are terms a public repository states
rather than a story it tells, they are what the platform and a stranger look for by name, and
their shape is fixed by convention rather than chosen: [`LICENSE`](LICENSE),
[`CONTRIBUTING.md`](CONTRIBUTING.md), [`SECURITY.md`](SECURITY.md),
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) and [`PACKAGE.md`](PACKAGE.md), the readme packed into
every NuGet package. They are not deletable as unratified prose, and they are held to the same
declarative current-state rule as everything else.

**Documentation is declarative current-state.** It describes the engine as it is, as if written
fresh: no history sections, no changelogs, no supersession notes, no design-rationale essays, no
decision records — anywhere in this repository, code comments included. Design rationale and
decision history are maintained internally by JAG Studios in the design repo's decision ledgers.

**No doc-comment forward-references a feature that has not landed.** `docs/architecture.md`
deliberately holds capabilities designed but not yet built; the API reference describes only what
compiles today.

**Whatever prose survives, update it in the same change** as the code it describes. Documentation
that lags is worse than none: it is confidently wrong.

## The boundaries

**`Capsule.Core` never references MonoGame, or anything else.** It has no package references at
all and nothing gates their absence, so this is the one boundary held by review rather than by a
build. Do not add one. Logic that needs a device belongs in `Capsule.Runtime`.

**`Capsule.Maps` takes no package reference either.** It is the format a game links at runtime, so
an authoring tool's dependency reaching it would ship in every game. Parsing an authoring format
belongs in `Capsule.Maps.Cli`, which a game never references.

## Placement — no engine code without a call site

**Engine code lands only alongside a consuming game call site in the same change-set.** A
subsystem with no caller is a guess about a future need, and it calcifies before anything
corrects it. If a game does not need it yet, the direction may be recorded in
[`docs/architecture.md`](docs/architecture.md) — the code may not exist.

## Publishable as-is

**Every commit should be able to go public unchanged.** No game vocabulary anywhere on the public
surface, in-repo docs that stand without studio context, CI a stranger could run, every public
member defensible in public. This is a quality bar, never a scope bar: a generalisation, hook or
option for a hypothetical external user is still speculative engine and still fails the placement
rule above.

## Tests

**A test guards what the code cannot state for itself** — a contract, an invariant, a boundary or
a hazard. One that restates what the code visibly does is deleted like any other restatement, the
coverage gate is a floor and never a target, and no test asserts a game's tuning values or
authored content.

## Where the conventions live

**This repository is the authority on how Capsule is built and used.** MonoGame is a substrate
Capsule hides, so substrate conventions are Capsule's own and are held here, not deferred to
anything upstream — `Directory.Build.props` for nullable, warnings-as-errors, the analyzer level
and lock files, where a warning is fixed or suppressed with a justification at the suppression
site, and the prose surface above for everything else. A deviation is recorded in the same
change, never silent.
