# Agent rules

These rules cover judgments the build cannot enforce. Read diagnostics before adding prose: architecture, role legality, public API documentation, and packaging are already gated.

## Building a game

Consuming Capsule is documented in [`docs/consuming-capsule.md`](docs/consuming-capsule.md); the runnable minimal game is [`samples/MinimalGame/`](samples/MinimalGame/).

## Scope

Engine features are initiated by a consuming game's need, never bounded by it: what lands must meet the bar of a high-class open-source engine — peak performance, a modern feature-set, no knowingly suboptimal or brute-force implementations, no half-built features. That bar is not a compatibility ceremony: JAG's own games are the only considered consumers, so break a public API whenever the better design needs it and migrate the consuming game in the same wave. Do not add hooks, options, or abstractions no game has asked for. Keep public names game-agnostic, and leave game policy in the game.

## Documentation

XML comments are the API reference and the only prose copy of public behavior. Keep them precise about units, ownership, lifecycle, exceptions, and non-obvious contracts; do not narrate signatures.

Markdown is limited to onboarding, cross-cutting architecture, build configuration, data formats, and standard project contracts. Keep all documentation declarative and current: no changelogs, migration notes, decision history, or forward references to unimplemented features.

Every shipped package carries a charter README: what it is, what is inside it, and where the deeper documentation lives — never API reference, and never substance that belongs to `docs/`.

Comments explain invariants and hazards the code cannot state. Delete walkthroughs, section labels, and commentary addressed to reviewers.

## Boundaries

The build enforces module direction and game-role purity. One boundary remains review-owned: parsers for authoring formats belong in `Capsule.Cli`, never in a runtime-linked assembly.

Warnings are fixed or suppressed with the reason at the suppression site. Every commit must remain publishable without studio-only context.

## Public surface

- A member is public iff a game calls it or any plausible 2D game must.
- Code the generators emit into the game is game code — members only it calls stay public and carry `[EditorBrowsable(Never)]`.
- Cross-assembly engine use is internal plus a per-member `InternalsVisibleTo` with its reason in the csproj.
- Test-only members are internal.
- Nothing calls it, delete it.
- Document model types are public because games author them in tests; the parser is not.
- Engine-owned state never has a public setter.

## Tests

Test contracts, invariants, boundaries, and failure modes. Do not test obvious implementation steps, target coverage mechanically, or assert a game's content and tuning in the engine suite.
