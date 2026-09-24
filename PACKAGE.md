![Capsule, a hero stepping out of a glowing capsule as a game world materializes around it](https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/docs/assets/capsule-hero.png)

# Capsule Engine

Capsule is a deterministic, code-first 2D game engine for C#. Scenes are authored in code or as scene documents. Gameplay never touches the graphics host, and it runs in headless tests.

| Package | Purpose |
| --- | --- |
| `JAG.Capsule` | Substrate-free gameplay APIs: simulation, input, rendering and audio contracts, save documents, tile grids, collision, and the world of scenes and entities. |
| `JAG.Capsule.Runtime` | The platform-neutral host: window, device, clock, input sampling, renderer, sound playback, scene hosting, and the `HostPlatform` contract. |
| `JAG.Capsule.Runtime.Desktop` | The desktop platform module a shell references: content beside the executable, saves and the crash log in the per-user local folder, window raising and focus, and sound following the default output. |
| `JAG.Capsule.Build` | The build tool, source generators and analyzers: asset naming, scene, sprite, font, audio and shader compilation, and atlas packing. |

`JAG.Capsule.Runtime` and `JAG.Capsule.Build` carry the [third-party notices](https://github.com/just-awesome-games/capsule-engine/blob/main/THIRD-PARTY-NOTICES.md) for the embedded default font and the atlas packer's image codecs.

Start with the [repository quickstart](https://github.com/just-awesome-games/capsule-engine#quick-start). The [README](https://github.com/just-awesome-games/capsule-engine#documentation) indexes the task pages.

Capsule is licensed under the [MIT License](https://github.com/just-awesome-games/capsule-engine/blob/main/LICENSE).
