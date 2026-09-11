# Capsule.Build

Build-time integration, generators, analyzers and the document process — one package a game references and never ships.

Contains: the build targets — `Capsule.Build.targets` and the `Capsule.DevelopmentOnly`, `Capsule.Compiler`, `Capsule.Icon` and `Capsule.ApiReference` files it imports — `Capsule.Generators` (including the analyzer), and this assembly: the one pass over the authoring plane the targets run per build, packed unlisted under `tools/`.

Referenced by: all game projects.

See [`docs/architecture.md`](../../docs/architecture.md) for the module map and determinism contract.
