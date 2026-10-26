# Capsule.Bench

The engine's performance harness, run by hand and never by CI or the commit hook. It is a Capsule game laid out as [`docs/build-and-publish.md`](../../docs/build-and-publish.md) describes, and each top-level scene is a workload. A scene declares its lane with `[Workload(...)]`, and the suite refuses one without it. A scene that declares several lanes is measured once in each. A lane's row is named for its class unless `Row` names a variant, which the class builds on its own document when `Workloads.Running` holds that name. `--scene` takes a row's name. A nested scene is part of the workload that declares it.

- `WorkloadKind.Simulation` runs headless with no window, device or media. It times 600 fixed steps after 180 warm-up steps. Between steps the driver builds the frame a presenting host would and times it apart.
- `WorkloadKind.Rendering` runs windowed for six real seconds on the attribute's surface: `Surface.Canvas360` (640x360 point-sampled, the default) or `Surface.Hd1080` (1920x1080 linear). It drops the first 120 frames and measures each frame's update, build and submission, its interval, and the bytes it allocates.
- `WorkloadKind.Paced` runs headless through the host's frame loop for six real seconds, paced to the step rate as a window presenting once a step would be. Each frame runs the steps its wall time bought, within the step bound and the step budget. A step that overruns its length shows in the frame interval a player would see. It drops the first second and measures frame interval, steps run, each frame's update and build, and the bytes it allocates.
- `Mix64` is not a scene. The suite runs it inside the suite's process, alongside the headless lane, and it times the host's sound mix one 512-frame block at a time with no device: 63 resident voices resampled at spread pitches and one stream.

The bench runs with `TieredCompilation=false`. Its absolute numbers differ from a tiered game's.

```text
dotnet run -c Release --project tests/Capsule.Bench -- suite --label <text> [--lane simulation|rendering|paced] [--uncapped]
```

`--lane` runs one lane, and the record's `lane` names it (`all` without the option). The windowed lane paces to vertical sync unless `--uncapped` is given, and then `intervalMs` is the host's true frame cost. The suite refuses `--uncapped` with a headless lane. Each workload runs in a child process on the engine's own command line, and `-- --scene Crowd1k [--uncapped]` runs one by hand. A record goes to `results/<UTC timestamp>.json` and is committed. Frame CSVs go under `artifacts/bench/` as `<row>.csv` and are not. The captures of `Still`'s rows go there as `<row>-<label>.png` and are not either, except a capture a later change is measured against, which is kept under `results/captures/<label>/`.

## Reading a record

A record is a header (`timestamp`, `label`, `lane`, `uncapped`, `commit`, `configuration`, `os`, `cpu`, `gpu`, `engineVersion`) and one row per workload. A row has `name`, `mode`, `surface`, `gen0Collections` over the measured window, and its lane's columns. The other lanes' columns are null.

- Headless: `steps`, `stepMs` {median, p95} for the fixed step alone, `viewMs` {median, p95} for building the frame after it, and `stepBytes` and `viewBytes`, the exact bytes the measured steps and builds allocated on their thread. A row that allocates by design says so in its workload's summary. Every other row reads 0.
- Windowed: `frames`, `drawMs` {median, p95, max} for `FrameRenderer.Draw` alone, `updateMs` {median, p95, max} for the frame's input, steps and host upkeep, `viewMs` {median, p95} for the frame's build over the frames that ran a step, `frameBytes`, the bytes the frame thread allocated over the measured frames, and `intervalMs` {median, p95, max} from frame start to frame start. `captureSha256` appears on the rows of `Still` (`Still`, `StillLit`, `StillShaded` and `StillHd`) and changes only when the renderer's output did.
- Paced: `frames`, `steps` for the fixed steps those frames ran, one a frame while the workload keeps pace, `updateMs` {median, p95, max}, `viewMs` {median, p95} for the frame's build over the frames that ran a step, `frameBytes`, and `intervalMs` {median, p95, max} from frame start to frame start.
- Mixer: `steps` for the blocks mixed, `stepMs` {median, p95} for one block, and `stepBytes`, the bytes the measured blocks allocated.

Changing a workload's definition resets its history. Compare by label and commit. Compare an uncapped record only with another uncapped record. A light workload's uncapped interval moves with the machine's state between sessions, and a change is measured against a build of the committed tree run back to back with it. Older records differ:

- Before 2026-09-18 they carry other columns.
- Before 2026-09-30 they carry no `lane` and cover both lanes.
- Headless rows before `alloc-check` carry no `stepBytes` or `viewBytes`.
- Headless rows before `perf-lazy-view` carry no `viewMs`, and their `stepMs` includes the frame build.
- Records before `shove-mask` carry no paced rows.
- Records before `rung26-mixer` carry no `Mix64` row.
- Records before `h0-gl` carry no `StillShaded` or `StillHd` row.
- Windowed and paced rows before `frame-columns` carry no `updateMs` or `frameBytes`. Their windowed rows carry no `viewMs`, and a paced row's `viewMs` covers every frame.

To add a workload, add a scene class with a `[Workload]` attribute under `Capsule.Bench.Logic/Scenes/`, composed from `Entities/`, `Components/`, `Cameras/` and `UI/` as a game would be. A level an editor would export is a `*.scene.json` under `Assets/Scenes/`, as `Stage` claims `stage.scene.json`. Commit a large generated grid as data, one row per line. Frame a workload with `ParkedCamera` unless camera travel is the point, and keep anything captured static.

## Profiling a workload

The `Soak` driver runs a headless workload for 20 real seconds after 180 warm-up steps, or for `--soak-seconds <n>`. It prints the steps and their mean, `threadBytes` and `processBytes` allocated, the collections per generation, and the managed heap as `heapStart`, `heapEnd` and `heapMax`. A heap that grows shows as `heapEnd` and `heapMax` above `heapStart`. `SoakView` also builds the frame between steps.

```text
dotnet build -c Release tests/Capsule.Bench
mkdir -p artifacts/bench/traces
dotnet-trace collect --profile dotnet-sampled-thread-time -o artifacts/bench/traces/Crowd1k.nettrace --show-child-io -- <full path to tests/Capsule.Bench/bin/Release/net10.0/Capsule.Bench.exe> --scene Crowd1k --headless --driver Soak
dotnet-trace report artifacts/bench/traces/Crowd1k.nettrace topN -n 30 [--inclusive]
```

`dotnet-trace` creates no directory and needs the shell's absolute path. `topN` ranks by exclusive time, and `--inclusive` by time with the method anywhere on the stack. A small inlined method shows up as its caller's time. The sampler's own pauses show up as `Thread.PollGC` time and count toward every method above it on the stack, such as a long `Array.Copy` of references. A method whose inclusive time is mostly `PollGC` needs a before-and-after `StepTimer` run before it counts as a cost. To find what allocates, collect with `--profile gc-verbose` and read the `GC/AllocationTick` stacks in PerfView. `--format Speedscope` writes a flame graph for speedscope.app. Traces stay under `artifacts/bench/`.

## Allocation in a NativeAOT build

The bench, the allocation tests and `dotnet run` all run JIT-compiled code, and a shipped game runs NativeAOT code. A call that allocates nothing under the JIT can allocate after NativeAOT compilation, and nothing on the JIT side reports it. `Marshal.SizeOf<T>()` allocates 32 bytes a call under NativeAOT and none under the JIT, for any `T`. Measure a published build before a release and after any upgrade of .NET or SDL. Publish the game with the development flags kept, then drive it windowed and headless:

```text
dotnet publish src/MyGame.Shell -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:CapsuleShipping=false -o artifacts/bench/aot
artifacts/bench/aot/MyGame.exe --driver <Name> --frames artifacts/bench/aot-window.csv 60
artifacts/bench/aot/MyGame.exe --driver <Name> --headless --frames artifacts/bench/aot-headless.csv 30
```

The frame CSV's `bytes` column is what the frame thread allocated since the previous frame. A steady allocation in the windowed run that the headless run lacks comes from presentation or media: the renderer, the window or the audio device. One in both can come from the steps, the frame build, the driver or the host's upkeep, and needs those split apart.

## Input latency

```text
dotnet run -c Release --project tests/Capsule.Bench -- latency [--label <text>] [--cap <hz>]
```

`latency` plays `Still` windowed for 25 seconds under vertical sync, or with no vertical sync at the frame cap `--cap` names. A thread in the run presses and releases F13 for 20 seconds, at random gaps of 30 to 110 ms and holds of 10 to 45 ms, after two seconds of warm-up. On Windows each press is a scancode `SendInput`, which reaches SDL through the OS queue as a player's does and is sent only while the window is in front. Leave it in front for the run. Elsewhere the press is an SDL event pushed straight into SDL's queue. The run measures each press from its event's SDL timestamp to the return of the present of the first frame whose steps consumed it. A record goes to `results/latency/<UTC timestamp>.json` with the suite's header less `lane` and `uncapped`, then `workload`, `vsync`, `cap`, `injection` (`SendInput` or `SDL_PushEvent`), `injected` and `presses` counts, `latencyMs` {median, p95, max}, and `injectedLatencyMs`, the same span from the injector's own clock, paired press for press and null when a press went unmeasured. Windows stamps an event with its message time, in whole ticks of a coarse clock, which puts `latencyMs` above `injectedLatencyMs`. Compare by label.

## Asset build

```text
dotnet run -c Release --project tests/Capsule.Bench -- assets [--label <text>] [--runs N]
```

`assets` times the asset build over a fixed-seed game it generates under `artifacts/bench/asset-corpus/`. It regenerates the corpus when `AssetCorpus.Version` changes. The corpus builds from this clone in source mode and holds eight atlases of 600 sprites, 400 sheets, 4,000 unpacked textures, 300 `r8` masks, 400 scene documents of a few hundred KB each, 400 sources of a toy `.room` importer over ten `.palette` files, 1,000 short `.wav` effects, the sample's `.ogg` tracks, six shaders and two fonts.

Each case times `dotnet build --no-restore` of the logic project after one untimed warm-up build. `cold` deletes `obj/.../capsule/` first. Then come `no-change`, one edit each of an unpacked texture, a packed sprite, an `r8` mask, a scene, a `.room` source, a `.palette` and a shader, and `touch-only`, which moves the write time of about 100 files. A row is the case's median over `--runs` (default 3, `cold` once): `case`, `wallMs` for the whole build, `toolMs` for the `CapsuleRunBuildTool` target, and `runs`. A record goes to `results/assets/<UTC timestamp>.json` with the suite's header less `uncapped`, plus `corpusVersion`, `corpusFiles` and `corpusBytes`.
