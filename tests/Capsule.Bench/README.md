# Capsule.Bench

The engine's performance harness: a Capsule game whose scenes are workloads, laid out as [`docs/build-and-publish.md`](../../docs/build-and-publish.md) lays a game out, run by hand and not by CI or the commit hook. Each top-level scene declares its lane with `[Workload(...)]`, and the suite refuses one without it. A scene nested in a workload is part of that workload. `WorkloadKind.Simulation` runs headless, with no window, device or media, for 180 warm-up and 600 measured fixed steps, each timed, so a row is one step in isolation. `WorkloadKind.Rendering` runs windowed for six real seconds on the surface the attribute names (`Surface.Canvas360`, 640x360 point-sampled, by default, or `Surface.Hd1080`, 1920x1080 linear), drops the first 120 frames while the window settles a late present, and measures submission and frame interval. Rendering is the lane that loads media. A workload isolates one subsystem and names it in its summary. The bench runs with `TieredCompilation=false`, so every frame runs steady-state code with no dynamic PGO, and absolute numbers differ from a tiered game's.

```text
dotnet run -c Release --project tests/Capsule.Bench -- suite --label <text> [--uncapped]
```

The default suite paces windowed workloads to the display's vertical sync. `--uncapped` passes the engine's `--uncapped` flag to every windowed workload, which then presents without waiting for vertical sync. Its `intervalMs` is the host's true frame cost instead of the display's rate. `-- --scene Crowd1k --uncapped` runs one workload the same way.

Each workload runs in a child process of the same binary on the engine's own command line (`--scene X --headless --driver StepTimer`, or `--scene X --frames <csv> 6`). Without `suite` the shell is an ordinary game, and `-- --scene Crowd1k` runs one workload windowed. A record goes to `results/<UTC timestamp>.json` and is committed. Frame CSVs and the `Still` capture go under `artifacts/bench/` and are not.

A record is a header (`timestamp`, `label`, `uncapped`, `commit`, `configuration`, `os`, `cpu`, `gpu`, `engineVersion`) and one `workloads` row per scene: `name`, `mode`, `surface`, then `steps` and `stepMs` {median, p95} on headless rows, `frames`, `drawMs` {median, p95, max} and `intervalMs` {median, p95, max} on windowed rows (null on the other lane), `gen0Collections` over the measured steps or frames, and `captureSha256` (`Still` rows only, and it changes only when the renderer's output did). `drawMs` is `FrameRenderer.Draw` by itself. `intervalMs` is frame start to frame start: the display's rate when the host keeps up, and the hitches in its tail. Changing a workload's definition resets its history, so compare by label and commit. Compare an uncapped record only with another uncapped record. Records before 2026-09-18 carry other columns.

To add a workload, add a scene class under `Capsule.Bench.Logic/Scenes/` with a `[Workload]` attribute, composed from `Entities/`, `Components/`, `Cameras/` and `UI/` as a game would. The suite discovers it from the generated scene registry and runs it by class name. What a level editor would export is a `*.scene.json` under `Assets/Scenes/` the scene claims by key, as `Stage` sits over `stage.scene.json`. A large grid is generated once by a throwaway script and committed as data, one row per line. Frame it with `ParkedCamera` unless camera travel is the point, and keep anything captured static.

## Asset build

```text
dotnet run -c Release --project tests/Capsule.Bench -- assets [--label <text>] [--runs N]
```

`assets` times the asset build, apart from the suite. It generates a fixed-seed game under `artifacts/bench/asset-corpus/` when that is absent or its version constant in `AssetCorpus.cs` has changed. The game builds from this clone in source mode, as [`docs/build-and-publish.md`](../../docs/build-and-publish.md) wires one: a logic project whose `Assets/` holds eight atlases of 600 small sprites, 400 sheets over them, 4,000 unpacked textures a third of them noise, 300 `r8` masks, 400 scene documents each carrying a few hundred KB of tile maps, 400 sources of a toy `.room` importer reading ten `.palette` files, 1,000 short `.wav` effects, the sample's `.ogg` tracks, six shaders and two fonts, and a build project running the importer.

Each case times `dotnet build` of the logic project with `--no-restore`, after one untimed warm-up build: `cold` with `obj/.../capsule/` deleted, then `no-change`, one edit each of an unpacked texture, a packed sprite, an `r8` mask, a scene, a `.room` source, a `.palette` and a shader, and `touch-only`, which moves the write time of about 100 files and changes no byte. Every edit writes new content, numbered by its run, and the next invocation writes the canonical file back first.

A row is the case's median over `--runs` (default 3, `cold` runs once): `case`, `wallMs` for the whole build, `toolMs` for the `CapsuleRunBuildTool` target from MSBuild's performance summary, and `runs`. A record goes to `results/assets/<UTC timestamp>.json` with the suite's header, less `uncapped`, plus `corpusVersion`, `corpusFiles` and `corpusBytes`, and is committed.
