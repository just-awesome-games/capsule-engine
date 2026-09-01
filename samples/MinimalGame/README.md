# MinimalGame

A complete Capsule game and the engine's consumer proof: a logic project, a shell, an authoring tree, and a headless smoke binary that CI publishes under NativeAOT and runs to verify the game boots.

## Repository shape

This sample demonstrates the layout prescribed in [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md) § Repository shape: logic and shell projects under `src/`, asset sources under `src/asset-sources/`, configuration in shared `Directory.Build.*` files.

## Running the game

From the engine repository root:

```sh
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell -p:CapsuleSourcePath=../..
```

To run against NuGet packages (after building and packing):

```sh
dotnet pack --configuration Release --output artifacts/packages
dotnet restore samples/MinimalGame/MinimalGame.slnx --configfile samples/MinimalGame/NuGet.config
dotnet build samples/MinimalGame/MinimalGame.slnx --configuration Release --no-restore
dotnet run --project samples/MinimalGame/src/MinimalGame.Shell --configuration Release --no-restore
```

The `MinimalGame.Smoke` executable is a device-free binary: it boots the game, steps it through a scripted input sequence, and asserts expected behavior, so the NativeAOT-published binary is *run*, not merely compiled, in CI.
