# Capsule.Bench

The engine's performance harness, run by hand and never by CI or the commit hook. It is a Capsule game laid out as [`docs/build-and-publish.md`](../../docs/build-and-publish.md) describes, and each top-level scene is a workload. A scene declares its lane with `[Workload(...)]`, and the suite refuses one without it. A nested scene is part of the workload that declares it.

- `WorkloadKind.Simulation` runs headless with no window, device or media. It times 600 fixed steps after 180 warm-up steps. Between steps the driver builds the frame a presenting host would and times it apart.
- `WorkloadKind.Rendering` runs windowed for six real seconds on the attribute's surface: `Surface.Canvas360` (640x360 point-sampled, the default) or `Surface.Hd1080` (1920x1080 linear). It drops the first 120 frames and measures submission and frame interval.

The bench runs with `TieredCompilation=false`, so absolute numbers differ from a tiered game's.

```text
dotnet run -c Release --project tests/Capsule.Bench -- suite --label <text> [--lane simulation|rendering] [--uncapped]
```

`--lane` runs one lane, and the record's `lane` names it (`all` without the option). The windowed lane paces to vertical sync unless `--uncapped` is given, and then `intervalMs` is the host's true frame cost. The suite refuses `--uncapped` with `--lane simulation`. Each workload runs in a child process on the engine's own command line, and `-- --scene Crowd1k [--uncapped]` runs one by hand. A record goes to `results/<UTC timestamp>.json` and is committed. Frame CSVs and the `Still` capture go under `artifacts/bench/` and are not.

## Reading a record

A record is a header (`timestamp`, `label`, `lane`, `uncapped`, `commit`, `configuration`, `os`, `cpu`, `gpu`, `engineVersion`) and one row per workload. A row has `name`, `mode`, `surface`, `gen0Collections` over the measured window, and its lane's columns. The other lane's columns are null.

- Headless: `steps`, `stepMs` {median, p95} for the fixed step alone, `viewMs` {median, p95} for building the frame after it, and `stepBytes` and `viewBytes`, the exact bytes the measured steps and builds allocated on their thread. A row that allocates by design says so in its workload's summary. Every other row reads 0.
- Windowed: `frames`, `drawMs` {median, p95, max} for `FrameRenderer.Draw` alone, and `intervalMs` {median, p95, max} from frame start to frame start. `captureSha256` appears on `Still` rows and changes only when the renderer's output did.

Changing a workload's definition resets its history, so compare by label and commit. Compare an uncapped record only with another uncapped record. Older records differ:

- Before 2026-09-18 they carry other columns.
- Before 2026-09-30 they carry no `lane` and cover both lanes.
- Headless rows before `alloc-check` carry no `stepBytes` or `viewBytes`.
- Headless rows before `perf-lazy-view` carry no `viewMs`, and their `stepMs` includes the frame build.

To add a workload, add a scene class with a `[Workload]` attribute under `Capsule.Bench.Logic/Scenes/`, composed from `Entities/`, `Components/`, `Cameras/` and `UI/` as a game would be. A level an editor would export is a `*.scene.json` under `Assets/Scenes/`, as `Stage` claims `stage.scene.json`. Commit a large generated grid as data, one row per line. Frame a workload with `ParkedCamera` unless camera travel is the point, and keep anything captured static.

## Profiling a workload

The `Soak` driver runs a headless workload for 20 real seconds after 180 warm-up steps, or for `--soak-seconds <n>`. It prints the steps and their mean, `threadBytes` and `processBytes` allocated, the collections per generation, and the managed heap as `heapStart`, `heapEnd` and `heapMax`. A heap that grows shows as `heapEnd` and `heapMax` above `heapStart`. `SoakView` also builds the frame between steps.

```text
dotnet build -c Release tests/Capsule.Bench
mkdir -p artifacts/bench/traces
dotnet-trace collect --profile dotnet-sampled-thread-time -o artifacts/bench/traces/Crowd1k.nettrace --show-child-io -- <full path to tests/Capsule.Bench/bin/Release/net10.0/Capsule.Bench.exe> --scene Crowd1k --headless --driver Soak
dotnet-trace report artifacts/bench/traces/Crowd1k.nettrace topN -n 30 [--inclusive]
```

`dotnet-trace` creates no directory and needs the shell's absolute path. `topN` ranks by exclusive time, and `--inclusive` by time with the method anywhere on the stack. A small inlined method shows up as its caller's time. To find what allocates, collect with `--profile gc-verbose` and read the `GC/AllocationTick` stacks in PerfView. `--format Speedscope` writes a flame graph for speedscope.app. Traces stay under `artifacts/bench/`.

## Asset build

```text
dotnet run -c Release --project tests/Capsule.Bench -- assets [--label <text>] [--runs N]
```

`assets` times the asset build over a fixed-seed game it generates under `artifacts/bench/asset-corpus/`. It regenerates the corpus when `AssetCorpus.Version` changes. The corpus builds from this clone in source mode and holds eight atlases of 600 sprites, 400 sheets, 4,000 unpacked textures, 300 `r8` masks, 400 scene documents of a few hundred KB each, 400 sources of a toy `.room` importer over ten `.palette` files, 1,000 short `.wav` effects, the sample's `.ogg` tracks, six shaders and two fonts.

Each case times `dotnet build --no-restore` of the logic project after one untimed warm-up build. `cold` deletes `obj/.../capsule/` first. Then come `no-change`, one edit each of an unpacked texture, a packed sprite, an `r8` mask, a scene, a `.room` source, a `.palette` and a shader, and `touch-only`, which moves the write time of about 100 files. A row is the case's median over `--runs` (default 3, `cold` once): `case`, `wallMs` for the whole build, `toolMs` for the `CapsuleRunBuildTool` target, and `runs`. A record goes to `results/assets/<UTC timestamp>.json` with the suite's header less `uncapped`, plus `corpusVersion`, `corpusFiles` and `corpusBytes`.
