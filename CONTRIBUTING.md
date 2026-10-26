# Contributing to Capsule

Capsule is developed for JAG Studios' games in public. A change is accepted when it improves an existing engine capability or its documentation. A new subsystem, hook or option needs a consuming game's use case. Open an issue for one first. The rules a change is held to are in [`AGENTS.md`](AGENTS.md).

## Setup

Install a .NET 10 SDK on Windows, macOS or Linux. [`global.json`](global.json) accepts any feature band from 10.0.100 up. A distribution package such as Ubuntu's `dotnet-sdk-10.0` works. Then, once per clone:

```text
git config core.hooksPath .githooks
```

Until this is set, Git ignores `.githooks/` and a commit passes with no report.

A NativeAOT publish on Windows needs the native toolchain [`docs/build-and-publish.md`](docs/build-and-publish.md#publishing) names.

## The gate

[`.githooks/pre-commit`](.githooks/pre-commit) gates every commit: a locked restore, the build, the format check, and the tests. CI adds pack, then on Windows, Linux and macOS NativeAOT publishes of the sample shell from those packages, which it plays. The smoke, `tests/Capsule.AotSmoke`, asserts both directions of the `CapsuleShipping` axis. CI runs it once from an ordinary build and once published on Linux. Releases follow [`RELEASING.md`](RELEASING.md).

The runtime embeds its sprite shader precompiled in every GPU format, as the files under `src/Capsule.Runtime/Rendering/Shaders/`. A test fails when the shader template, compiler or tools change them. Running the tests with `CAPSULE_UPDATE_ENGINE_SHADER=1` regenerates the files. Review them and commit them.

`Capsule.Runtime` refuses an implicit location or a native binding at compile time through its banned-API list, [`src/Capsule.Runtime/BannedSymbols.txt`](src/Capsule.Runtime/BannedSymbols.txt). Its `Platform/` folder alone is exempt, and holds the SDL3 binding and the operating-system calls the default platform makes.

Wall-clock performance is measured by hand with [`tests/Capsule.Bench`](tests/Capsule.Bench/README.md), whose records are committed.

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
