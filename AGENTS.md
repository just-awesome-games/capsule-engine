# Agent rules

These rules cover judgments the build cannot enforce. Read diagnostics before adding prose: architecture, role legality, public API documentation, and packaging are already gated.

## Scope

Engine code lands with a consuming game call site. Do not add hooks, options, or abstractions for hypothetical consumers. Keep public names game-agnostic, and leave game policy in the game.

## Documentation

XML comments are the API reference and the only prose copy of public behavior. Keep them precise about units, ownership, lifecycle, exceptions, and non-obvious contracts; do not narrate signatures.

Markdown is limited to onboarding, cross-cutting architecture, build configuration, data formats, and standard project contracts. Keep all documentation declarative and current: no changelogs, migration notes, decision history, or forward references to unimplemented features.

Comments explain invariants and hazards the code cannot state. Delete walkthroughs, section labels, and commentary addressed to reviewers.

## Boundaries

The build enforces module direction and game-role purity. One boundary remains review-owned: parsers for authoring formats belong in `Capsule.Maps.Cli`, never in the runtime-linked `Capsule.Maps` assembly.

Warnings are fixed or suppressed with the reason at the suppression site. Every commit must remain publishable without studio-only context.

## Tests

Test contracts, invariants, boundaries, and failure modes. Do not test obvious implementation steps, target coverage mechanically, or assert a game's content and tuning in the engine suite.
