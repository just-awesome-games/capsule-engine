# Capsule.Build

Build-time integration, generators, analyzers and the document process: one package every game project references and never ships.

Contains the build targets — `Capsule.Build.targets` and the `Capsule.DevelopmentOnly`, `Capsule.Compiler`, `Capsule.Icon` and `Capsule.ApiReference` files it imports — `Capsule.Generators` including the analyzer, and this assembly, the one pass over the authoring plane the targets run per build, packed unlisted under `tools/`.

Referenced by all game projects. The build properties and the authoring pipeline are in [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md); the logic boundary it enforces in [`docs/architecture.md`](../../docs/architecture.md).
