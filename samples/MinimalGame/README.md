# MinimalGame

A small, complete Capsule platformer laid out as [`docs/build-and-publish.md`](../../docs/build-and-publish.md)
describes: a title menu, an options screen and two rooms to walk, jump and shoot through. CI builds it
against the shipped packages. Start reading at `GameBoot.cs` and `Scenes/`.

## Running

From the engine repository root, against engine source:

```sh
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --scene scenes/room --driver Walkthrough --headless
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --scene scenes/halls/hall --payload WestArrival --driver Walkthrough --headless
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --replay samples/MinimalGame/tests/MinimalGame.Tests/Recordings/Walkthrough.recording --headless --trace artifacts/player.csv 60..120 Player
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -- --replay samples/MinimalGame/tests/MinimalGame.Tests/Recordings/Walkthrough.recording --headless --capture artifacts/frames 60..62
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

Jump and shoot can be rebound under Options on the title menu, where the list scrolls with the wheel or its bar.
Delete, the north pad button or a right-click on a binding's row puts its default back.
Options also sets the music, effects, screen shake and rumble levels, where 0 turns each off, and switches the
language between English and German, fullscreen, the window's size, vertical sync and a frame cap that includes the
display's own refresh rate, and the game opens as they were left. It starts in the system's language, and
`-- --language de` starts it in German.
