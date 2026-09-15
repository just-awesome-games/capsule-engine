# Workflow

Capsule has no editor, and `dotnet` is the whole command surface: no Capsule CLI, no task runner. Any C# IDE runs and tests a game through its ordinary run and test affordances; every command below is the editor-free path from a game's repository root, with `MyGame` standing for the game's project name.

| Task | Command |
| --- | --- |
| Clone and first run | `dotnet restore --locked-mode`, then `dotnet run --project src/MyGame.Shell`. |
| Run | `dotnet run --project src/MyGame.Shell`; `--no-build` skips the graph evaluation and starts the last build as it stands. |
| Run a named scene | `dotnet run --project src/MyGame.Shell -- --scene <Name>`. |
| Run headless with a driver | `dotnet run --project src/MyGame.Shell -- --headless --driver <Name>` ([`headless-play.md`](headless-play.md)). |
| Test | `dotnet test`. |
| Format check / fix | `dotnet format --verify-no-changes` / `dotnet format`. |
| The gate | `sh hooks/pre-commit`: restore, build, format check, tests. |
| Source or package mode | Set or remove `CapsuleSourcePath`; `CapsuleUsePackages=true` forces the package lane ([`consuming-capsule.md`](consuming-capsule.md#package-and-source-modes)). |
| Ship | The `dotnet publish` line in [`consuming-capsule.md`](consuming-capsule.md#publishing). |
| Debug | The development overlay in the window and the console the game was launched from ([`debugging.md`](debugging.md)). |

The gate is also the commit hook, once per clone: `git config core.hooksPath hooks`. Git ignores `hooks/` until it is set, and an unconfigured clone commits straight past it without reporting anything.

Designer-owned tunables are plain C# — the sample's `src/MinimalGame.Game/Entities/PlayerTuning.cs` is the pattern — and the engine version is pinned once, in `Directory.Build.props`.
