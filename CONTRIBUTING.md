# Contributing to Capsule

Capsule is developed for JAG Studios' games in public. A change is accepted when it improves an existing engine capability or its documentation; a new subsystem, hook or option needs a consuming game's use case — open an issue first. The rules a change is held to are in [`AGENTS.md`](AGENTS.md).

## Setup

Install the .NET SDK selected by [`global.json`](global.json). Then, once per clone:

```text
git config core.hooksPath .githooks
```

Git ignores `.githooks/` until this is set, and an unconfigured clone commits straight past the hook without reporting anything.

A NativeAOT publish on Windows also needs the Visual Studio Installer directory (`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`) on `PATH`, or the ILC link step fails with `MSB3073`.

## The gate

[`.githooks/pre-commit`](.githooks/pre-commit) gates every commit: a locked restore, the build, the format check, and the tests. CI adds pack, the package-mode sample, and NativeAOT publishes of the sample shell and `tests/Capsule.AotSmoke`, whose binary it then runs. Releases follow [`RELEASING.md`](RELEASING.md).

Wall-clock performance is measured by hand with [`tests/Capsule.Bench`](tests/Capsule.Bench/README.md), whose records are committed.

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
