# Capsule.Generators

Capsule's source generators. They read a compilation and emit the registries a game would
otherwise hand-maintain, so a game keeps no registration table and uses no reflection to boot.

## Inside

- `RegistryGenerator` — the entry point; `AssetRegistryGenerator` covers shipped assets.
- `SceneRegistrySource`, `EntityRegistrySource`, `AssetRegistrySource`, `RegistryProviderSource`,
  `CapsuleBootSource` — one emitter per generated artifact.
- `SceneModel`, `EntityModel`, `AssetModel`, `Symbols`, `TypeNaming` — the compilation-shaped
  inputs and the naming rules.
- `RegistryDiagnostics` — the `CAP0xx` diagnostics a malformed claim reports.

The emitted namespaces are `Capsule.Scenes.Generated` and `Capsule.Runtime.Generated`; they are
consumer-facing surface, and this assembly's own name is not.

## How it ships

As an analyzer in the `JAG.Capsule.Build` package; see [`../Capsule.Build/`](../Capsule.Build/README.md).
`build/Capsule.Scenes.targets` wires it into a game on the roles it declares, and it reaches no
game output. `netstandard2.0`, because the compiler loads it into its own process.

## Further reading

What a game declares to receive the generated registries:
[`docs/consuming-capsule.md`](../../docs/consuming-capsule.md).
