# Capsule.Build

Build-time integration, generators, analyzers and the scene-document process — one package a game references and never ships, and one project that is both the package and the process.

Contains: the build targets, `Capsule.Generators` (including the analyzer), and this assembly — the scene-document process the targets run, packed unlisted under `tools/`.

Referenced by: all game projects.

See [`docs/architecture.md`](../../docs/architecture.md) for the module map and determinism contract.
