# Contributing to Capsule

Capsule is developed for JAG Studios' games in public. Changes are accepted when they improve an existing engine capability or its documentation. New subsystems, hooks, and options need a consuming game use case; open an issue before investing in a speculative feature.

## Build

Install the .NET SDK selected by [`global.json`](global.json), then run:

```text
dotnet restore --locked-mode
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet pack --configuration Release --output artifacts/packages
```

Activate the local hook once per clone:

```text
git config core.hooksPath hooks
```

CI also verifies Release builds, 80% aggregate line coverage over the runtime modules, locked restores, package and project-reference consumers, NativeAOT execution on Linux and Windows, package contents, and performance workloads. The workflow in [`.github/workflows/ci.yml`](.github/workflows/ci.yml) is the executable authority.

## Expectations

- Public members in consumer-facing assemblies require XML documentation. That documentation is the API reference.
- A behavior change includes the test that would have caught its absence; a fix includes the test that would have caught the bug.
- Game-specific policy and speculative generalization do not belong in the engine.
- Warnings are errors. A necessary suppression includes its reason at the suppression site.
- Pull requests stay focused and state breaking changes plainly. Capsule is pre-1.0, so public APIs may change.

Additional repository rules are in [`AGENTS.md`](AGENTS.md).

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
