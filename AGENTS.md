# Agents

Operating rules for anyone changing this repository, human or agent. Every one is a rule no
compiler, test or CI job can enforce for you. Everything that *can* be enforced is, and is not
restated here — read the failure.

## Code is the documentation plane

**The XML doc comment is the single copy of the API surface.** `CS1591` is an error in every
assembly a game compiles against, so every public member is already documented where it is
defined. Markdown that narrates the API is a second copy that will drift, and is deleted rather
than corrected. Prose keeps only what code cannot carry: build configuration, data formats, and
the rules on this page. A short example that compiles is not narration — a reader on the web has
no IntelliSense — but the paragraphs explaining one are.

**The whole narrative prose surface is this file, [`README.md`](README.md),
[`docs/architecture.md`](docs/architecture.md),
[`docs/consuming-capsule.md`](docs/consuming-capsule.md) and
[`Capsule.Maps/README.md`](Capsule.Maps/README.md)** — capped in volume as much as in count. Each
states one job no other file does; one that cannot is deleted rather than trimmed. A new prose
document, or a second module README, needs a maintainer-ratified reason.

**The contract files sit outside that cap** — [`LICENSE`](LICENSE),
[`CONTRIBUTING.md`](CONTRIBUTING.md), [`SECURITY.md`](SECURITY.md),
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md), and [`PACKAGE.md`](PACKAGE.md), the readme packed into
every NuGet package. A stranger looks for them by name and convention fixes their shape. Every
rule below still binds them.

**Documentation is declarative current-state**: no history, no changelogs, no supersession notes,
no rationale essays, no decision records — anywhere, code comments included. Decision history is
maintained internally by JAG Studios in the design repo's ledgers.

**No doc-comment forward-references a feature that has not landed.**
[`docs/architecture.md`](docs/architecture.md) is the one place a capability designed but not built
may be recorded; the API reference describes only what compiles today.

**Whatever prose survives, update it in the same change** as the code it describes. Documentation
that lags is worse than none: it is confidently wrong.

## Placement — no engine code without a call site

**Engine code lands only alongside a consuming game call site in the same change-set.** A subsystem
with no caller is a guess about a future need, and it calcifies before anything corrects it.

## Publishable as-is

**Every commit should be able to go public unchanged**: no game vocabulary on the public surface,
docs that stand without studio context, CI a stranger could run. This is a quality bar, never a
scope bar — a hook or option for a hypothetical external user is still speculative engine, and
still fails the placement rule.

## Tests

**A test guards what the code cannot state for itself** — a contract, an invariant, a boundary or a
hazard. One that restates what the code visibly does is deleted like any other restatement, the
coverage floor is never a target, and no test asserts a game's tuning values or authored content.

## Boundaries

Module dependency direction, the purity of the modules `JAG.Capsule` ships, and the legality of a
consuming project's roles are gated by
[`build/Capsule.Architecture.targets`](build/Capsule.Architecture.targets) and `Capsule.Analyzers`.

The one boundary no build holds: **an authoring format's parser belongs in `Capsule.Maps.Cli`,
which a game never references.** `Capsule.Maps` is what a game links at runtime, so a parser
reaching it would ship in every game.

## Where the conventions live

**This repository is the authority on how Capsule is built and used.** MonoGame is a substrate
Capsule hides, so substrate conventions are Capsule's own and are held here, not deferred
upstream. A warning is fixed, or suppressed with its justification at the suppression site. A
deviation is recorded in the same change, never silent.
