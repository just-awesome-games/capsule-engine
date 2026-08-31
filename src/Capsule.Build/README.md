# Capsule.Build

The pack root for `JAG.Capsule.Build`, the dev-time half of the engine. It holds no code of its
own: it is the one project that collects Capsule's build integration, generators, analyzers and
CLI into a package a game references and never ships.

## Inside

- The `build/*.targets` files, packed into `buildTransitive/` so a game gets them by referencing
  the package. `Capsule.Build.targets` is the entry point; the rest wire scene-document import,
  asset shipping, generators, analyzers and the default icons.
- `Capsule.Cli` under `tools/`, `Capsule.Generators` and `Capsule.Analyzers` under `analyzers/`.

A game declares its roles — `CapsuleGameLogic` on the logic assembly, `CapsuleGameShell` on the
executable — and everything above is gated on those, so a generator or analyzer added to the
engine is never new wiring in the game.

## How it ships

As the `JAG.Capsule.Build` package. It carries no build output of its own and no dependencies,
and nothing it contains reaches a published game.

## Further reading

Project wiring, roles and properties: [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md).
