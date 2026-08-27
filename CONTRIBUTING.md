# Contributing to Capsule

Capsule is built for JAG Studios' own games, in the open. That shapes what lands: a change is
judged by whether it makes a JAG game better, and every module here exists because a game needed
it. A generalisation, hook or option for a hypothetical user is still speculative engine, and it
is declined however clean it is. Contributions that fix a defect, close a gap in what already
ships, or improve documentation are welcome on their merits, from anyone.

If you are unsure whether an idea fits, open an issue before writing the code. It costs you
nothing and it is the fastest way to find out.

## Building

Install the .NET SDK selected by [`global.json`](global.json). Nothing else: no editor, no engine
SDK, no MonoGame installation.

```
dotnet restore --locked-mode
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Activate the pre-commit hook once per clone:

```
git config core.hooksPath hooks
```

The hook runs the fast subset — a locked restore, a Debug build, the format check and the tests —
so the mistakes that are cheap to catch are caught before a push. It is not the full gate: CI also
builds Release, packs, builds the three package-consumer lanes, holds coverage to its floor, runs
the performance gates, and runs the Windows and NativeAOT legs. A green commit can still meet a
red pull request, and the list below is the one that decides.

## The gates a change must pass

All of these are enforced in [CI](.github/workflows/ci.yml), and none is optional.

- **Build clean in Debug and Release.** Warnings are errors, the analyzer level is `latest`, and
  code style is enforced in the build. The two configurations run different analyzer sets, so
  both are built.
- **`dotnet format --verify-no-changes`.** Formatting is not a review topic.
- **Tests pass, and line coverage stays at or above 80%** over `Capsule.Core`, `Capsule.Maps`,
  `Capsule.Runtime`, `Capsule.Scenes` and `Capsule.Verify`. The floor is a floor, never a target:
  a test that restates what the code visibly does is deleted like any other restatement.
- **Restore is locked.** `dotnet restore --locked-mode` must succeed, so a dependency change
  arrives with its lock files in the same commit.
- **The package consumer builds three ways** — against the published packages, against packages
  forced from a local pack, and against project references into this clone — proving the package
  surface and the source surface stay the same surface.
- **The NativeAOT publish runs.** A device-free consumer is published ahead-of-time and executed
  on Linux and Windows; it exits non-zero on a failed assertion or a blown allocation budget.

## Conventions

The rules a compiler cannot enforce live in [`AGENTS.md`](AGENTS.md), and they apply to human
contributors exactly as they do to agents. The ones that most often surprise a first change:

- **`Capsule.Core` and `Capsule.Maps` take no package references.** Anything that needs a device
  belongs in `Capsule.Runtime`; anything that parses an authoring format belongs in
  `Capsule.Maps.Cli`.
- **Engine code lands with a call site.** A subsystem no game uses yet is a guess, and it
  calcifies before anything corrects it.
- **Documentation is declarative current-state.** No changelogs, no history sections, no
  supersession notes, no design-rationale essays — anywhere, code comments included. Describe the
  engine as it is, as if written fresh.
- **A comment states what the code cannot** — an invariant, a unit, a hazard, a non-obvious why.
  Narration of the next line is deleted.
- **Public members carry XML documentation.** `CS1591` is an error in the assemblies a game
  compiles against: the doc comments are the API reference a consumer reads in their editor. The
  tooling carried inside `JAG.Capsule.Build` as `tools/` and `analyzers/` assets is outside the
  gate, because nothing compiles against it.
- **Prose that survives is updated in the same change** as the code it describes.

## Pull requests

Keep a pull request to one change with one reason. Describe what a consuming game can now do, or
what it could do wrongly before, and let the diff speak for the rest. New behaviour arrives with
the test that would have caught its absence; a bug fix arrives with the test that would have
caught the bug.

Capsule is pre-1.0 and its public API moves. A change that breaks a consuming game is acceptable
when it is the right change, and it is stated plainly in the pull request rather than discovered
downstream.

By contributing you agree that your contribution is licensed under the
[MIT License](LICENSE), like the rest of the repository.
