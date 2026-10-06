# MinimalGame

A small, complete Capsule game laid out as [`docs/build-and-publish.md`](../../docs/build-and-publish.md)
describes. Each engine feature is played here, and CI builds it against the shipped packages. Start
reading at `GameBoot.cs` and `Scenes/`.

## Running

From the engine repository root, against engine source:

```sh
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --scene scenes/room --driver Walkthrough --headless
dotnet test samples/MinimalGame/MinimalGame.slnx
```

Against the NuGet packages:

```sh
dotnet pack --configuration Release --output artifacts/packages
dotnet restore samples/MinimalGame/MinimalGame.slnx --configfile samples/MinimalGame/NuGet.config -p:CapsuleSourcePath=
dotnet build samples/MinimalGame/MinimalGame.slnx --configuration Release --no-restore -p:CapsuleSourcePath=
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell --configuration Release --no-restore -p:CapsuleSourcePath=
```

The gate is `sh hooks/pre-commit` from the sample root.

## Controls

- Move: A/D, the arrows, the d-pad or the left stick.
- Jump: Space or the south pad button.
- Drop through a ledge: hold down and press Jump.
- Shoot: the left mouse button or the west pad button.
- Pause: Escape or Start.
- Change rooms: walk into the dark doorway at the room's far right, past the belt that pushes back.

Jump and shoot can be rebound under Options on the title menu.
