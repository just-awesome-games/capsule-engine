![Capsule, a hero stepping out of a glowing capsule as a game world materializes around it](https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/docs/assets/capsule-hero.png)

# Capsule Engine

Capsule is a deterministic, code-first 2D game engine for C#. Scenes are authored in code or as scene documents, and gameplay is kept separate from the graphics host so it can run in headless tests.

| Package | Purpose |
| --- | --- |
| `JAG.Capsule` | Substrate-free gameplay APIs: simulation, input, rendering and audio contracts, save documents, tile grids, collision, and the world of scenes and entities. |
| `JAG.Capsule.Runtime` | The platform-neutral host: window, device, clock, input sampling, renderer, sound playback, scene hosting, and the `HostPlatform` contract. |
| `JAG.Capsule.Runtime.Desktop` | The desktop platform module a shell references: content beside the executable, saves and the crash log in the per-user local folder, window raising and focus, and sound following the default output. |
| `JAG.Capsule.Build` | Analyzers, generators, asset hooks, atlas packing, and scene and audio import. |

`JAG.Capsule.Runtime` and `JAG.Capsule.Build` carry the [third-party notices](https://github.com/just-awesome-games/capsule-engine/blob/main/THIRD-PARTY-NOTICES.md) for the embedded default font and the atlas packer's image codecs.

Start with the [repository quickstart](https://github.com/just-awesome-games/capsule-engine#quick-start), then [getting started](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/getting-started.md), [input](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/input.md), [rendering](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/rendering.md), [audio](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/audio.md), [collision](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/collision.md), [assets](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/assets.md), [scene authoring](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/scenes.md), [persistence](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/persistence.md), [build and publish](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/build-and-publish.md), and [testing a game](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/testing.md).

Capsule is licensed under the [MIT License](https://github.com/just-awesome-games/capsule-engine/blob/main/LICENSE).
