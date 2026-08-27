# Contributing to Capsule

Capsule is built for JAG Studios' own games, in the open. A change is judged by whether it makes a
JAG game better, and every module here exists because a game needed it: a hook or option for a
hypothetical user is speculative engine and is declined however clean it is. Fixes, gaps in what
already ships, and documentation improvements are welcome on their merits, from anyone. If you are
unsure whether an idea fits, open an issue before writing the code.

## Building

Install the .NET SDK selected by [`global.json`](global.json). Nothing else: no editor, no engine
SDK, no MonoGame installation.

```
dotnet restore --locked-mode
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet pack --configuration Release --output artifacts/packages
```

Activate the pre-commit hook once per clone:

```
git config core.hooksPath hooks
```

It runs the fast subset — locked restore, Debug build, format check, tests — so a green commit can
still meet a red pull request. The gates below are the ones that decide.

With coverage, as CI runs it:

```
dotnet test -p:CollectCoverage=true "-p:Include=[Capsule.Core]*%2c[Capsule.Maps]*%2c[Capsule.Runtime]*%2c[Capsule.Scenes]*" "-p:ExcludeByFile=**/Capsule.Runtime/CapsuleGame.cs%2c**/Capsule.Runtime/Rendering/FrameRenderer.cs" -p:CoverletOutputFormat=cobertura -p:Threshold=80 -p:ThresholdType=line -p:ThresholdStat=total
```

`CapsuleGame` and `FrameRenderer` need a live window and device, so they are excluded by file
rather than the assembly by name — a new device-free type in `Capsule.Runtime` is gated the day it
lands.

## The gates a change must pass

All are enforced in [CI](.github/workflows/ci.yml), and none is optional.

- **Build clean in Debug and Release.** Warnings are errors, the analyzer level is `latest`, and
  the two configurations run different analyzer sets.
- **`dotnet format --verify-no-changes`.** Formatting is not a review topic.
- **Tests pass, and line coverage holds at 80%** over `Capsule.Core`, `Capsule.Maps`,
  `Capsule.Runtime` and `Capsule.Scenes`. The floor is a floor, never a target.
- **Restore is locked**, so a dependency change arrives with its lock files in the same commit.
- **The package consumer builds three ways** — against the packages CI just packed, against those
  with `CapsuleUsePackages` overriding a source path, and against project references into this
  clone — proving the packaged surface and the source surface are one surface.
- **The NativeAOT publish runs.** A device-free consumer is published ahead-of-time and executed on
  Linux and Windows; it loads a real map, drives a scripted input sequence, and exits non-zero on a
  failed assertion.
- **The performance gates hold.** A stage-sized workload allocates nothing of the engine's own and
  reconstructs a scene without touching a file.

## Conventions

The rules a compiler cannot enforce live in [`AGENTS.md`](AGENTS.md), and they bind human
contributors exactly as they bind agents. The two that most often surprise a first change:

- **Public members carry XML documentation**, and those doc comments are the API reference — the
  only copy of it. Tooling inside `JAG.Capsule.Build` is outside the gate; nothing compiles
  against it.
- **Engine code lands with a call site.** A subsystem no game uses yet is a guess.

## Pull requests

Keep a pull request to one change with one reason, and let the diff speak for the rest. New
behaviour arrives with the test that would have caught its absence; a bug fix arrives with the test
that would have caught the bug.

Capsule is pre-1.0 and its public API moves. A change that breaks a consuming game is acceptable
when it is the right change, stated plainly in the pull request rather than discovered downstream.

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE), like
the rest of the repository.
