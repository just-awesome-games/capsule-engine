# Capsule

The pack root for `JAG.Capsule` — everything a game's logic is written against, in one package.

## The package boundary is the purity boundary

Capsule packs by purity, not by module. Every substrate-free module — one that reaches no
graphics device and takes no package reference at all — ships inside this single package, because
splitting them would make a game's reference list a matter of engine internals rather than of
what the game needs. The modules stay separate assemblies so their dependency direction keeps
being enforced by the compiler instead of by review.

This project's `ProjectReference` list is that admission list, and nothing else:
`Capsule.Core` and `Capsule.Scenes`. `build/Capsule.Architecture.targets` gates it, along with
each admitted module's own reference rules, so joining the package is a deliberate edit here and
never a side effect. The package declares no dependencies because no module admitted here takes
one.

## Inside

No code. The assemblies are composed into `lib/` from the list above, each with the XML
documentation that is its API reference, and the empty assembly this project produces ships too —
so a package reference and a project reference expose one graph.

## Further reading

The module map and the determinism contract: [`docs/architecture.md`](../../docs/architecture.md).
