![Capsule, a hero stepping out of a glowing capsule as a game world materializes around it](https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/docs/assets/capsule-hero.png)

# Capsule Engine

Capsule is a deterministic, code-first 2D game engine for C#. Scenes are authored in code or as scene documents. Gameplay never touches the graphics host, and it runs in headless tests.

| Package | Purpose |
| --- | --- |
| `JAG.Capsule` | Substrate-free gameplay APIs: simulation, input, rendering and audio contracts, save documents, tile grids, collision, and the world of scenes and entities. It also carries the build targets every game runs, and the source generators and analyzers. |
| `JAG.Capsule.Build` | The build a game's build project runs through `CapsuleBuild`: asset naming, scene, sprite, font, audio and shader compilation, and atlas packing. An importer package implements its `IAssetImporter`. Only build projects and importer libraries reference it. |
| `JAG.Capsule.Runtime` | The platform-neutral host: window, device, clock, input sampling, renderer, sound playback, scene hosting, and the `HostPlatform` contract. |
| `JAG.Capsule.Runtime.Desktop` | The desktop platform module a shell references: content beside the executable, saves and the crash log in the per-user local folder, window raising and focus, and sound following the default output. |

`JAG.Capsule.Runtime` carries the [third-party notices](https://github.com/just-awesome-games/capsule-engine/blob/main/THIRD-PARTY-NOTICES.md) for the embedded default font and the vendored Vorbis decoder.

Start with the [repository quickstart](https://github.com/just-awesome-games/capsule-engine#quick-start). The [README](https://github.com/just-awesome-games/capsule-engine#documentation) indexes the task pages.

Capsule is licensed under the [MIT License](https://github.com/just-awesome-games/capsule-engine/blob/main/LICENSE).
