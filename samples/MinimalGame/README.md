# MinimalGame

A small, complete Capsule game. It is where each engine feature gets played and reviewed, and CI builds
it against the shipped packages to prove them. Its layout is the one
[`docs/build-and-publish.md`](../../docs/build-and-publish.md) describes. Start reading at `GameBoot.cs`
and `Scenes/`.

## Running

From the engine repository root, against engine source:

```sh
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --scene room --driver Walkthrough --headless
dotnet test samples/MinimalGame/MinimalGame.slnx
```

Against the NuGet packages:

```sh
dotnet pack --configuration Release --output artifacts/packages
dotnet restore samples/MinimalGame/MinimalGame.slnx --configfile samples/MinimalGame/NuGet.config -p:CapsuleUsePackages=true
dotnet build samples/MinimalGame/MinimalGame.slnx --configuration Release --no-restore -p:CapsuleUsePackages=true
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell --configuration Release --no-restore -p:CapsuleUsePackages=true
```

The gate is `sh hooks/pre-commit` from the sample root.

## Controls

- Move: A/D, the arrows, the d-pad or the left stick.
- Jump: Space or the south pad button.
- Shoot: the left mouse button or the west pad button.
- Pause: Escape or Start.

Jump and shoot can be rebound under Options on the title menu.
