# Workflow

Capsule has no editor, and `dotnet` is the whole command surface: there is no Capsule CLI and no
task runner. Any C# IDE runs and tests a game through its ordinary run and test affordances. Every
command below is the editor-free path, run as written from a game's repository root, with `MyGame`
standing for the game's project name.

## Coming from Unity or Godot

| Editor idiom                | Capsule                                                                                                                                                        |
| --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Play button                 | `dotnet run --project src/MyGame.Shell`, or the IDE's run action on the shell project.                                                                         |
| Play a specific scene       | `dotnet run --project src/MyGame.Shell -- --scene <Name>`; the rest of the command line is in [`headless-play.md`](headless-play.md#the-standard-command-line). |
| Test Runner                 | `dotnet test`; an IDE's test explorer discovers the xunit project.                                                                                             |
| Console                     | The console the game was launched from, or the IDE's debug console when launched from one, and the development overlay in the window: [`debugging.md`](debugging.md). |
| Build & Run, Build settings | `dotnet publish` with the shipping line in [`consuming-capsule.md`](consuming-capsule.md#publishing).                                                          |
| Inspector, ScriptableObject | None. Tunables are plain C#; the sample's `src/MinimalGame.Game/Entities/PlayerTuning.cs` is the pattern.                                                      |
| Package Manager             | NuGet. The engine version is pinned once, in `Directory.Build.props`.                                                                                          |

## The everyday loop

| Task                        | Command                                                                                                                                                                        |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Clone and first run         | `dotnet restore --locked-mode`, then `dotnet run --project src/MyGame.Shell`.                                                                                                  |
| Run                         | `dotnet run --project src/MyGame.Shell`.                                                                                                                                       |
| Run a named scene           | `dotnet run --project src/MyGame.Shell -- --scene <Name>`.                                                                                                                     |
| Run headless with a driver  | `dotnet run --project src/MyGame.Shell -- --headless --driver <Name>`; drivers and the flags are in [`headless-play.md`](headless-play.md).                                     |
| Test                        | `dotnet test`.                                                                                                                                                                 |
| Format check                | `dotnet format --verify-no-changes`.                                                                                                                                           |
| Format fix                  | `dotnet format`.                                                                                                                                                               |
| The gate                    | `sh hooks/pre-commit`: restore, build, format check, tests.                                                                                                                    |
| Source mode or package mode | Set or remove `CapsuleSourcePath`; `CapsuleUsePackages=true` forces the package lane over a persistent source override: [`consuming-capsule.md`](consuming-capsule.md#package-and-source-modes). |
| Ship                        | The `dotnet publish` line in [`consuming-capsule.md`](consuming-capsule.md#publishing).                                                                                        |

The gate is also the commit hook. Activate it once per clone:

```text
git config core.hooksPath hooks
```

This is not optional: Git ignores `hooks/` until it is configured, and an unconfigured clone commits
straight past the hook without reporting anything.

## Start time

`dotnet run` evaluates the build graph on every launch, which typically costs two to three seconds
even when nothing has changed. `dotnet run --no-build --project src/MyGame.Shell` skips that and
starts the last build as it stands.
