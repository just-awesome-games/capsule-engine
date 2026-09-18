![Capsule — a hero stepping out of a glowing capsule as a game world materializes around it](https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/docs/assets/capsule-hero.png)

# Capsule Engine

Capsule is a deterministic, code-first 2D game engine for C#. Scenes are authored in code or as scene documents, and gameplay is kept separate from the graphics host so it can run in headless tests.

| Package | Purpose |
| --- | --- |
| `JAG.Capsule` | Substrate-free gameplay APIs: simulation, input, rendering and audio contracts, save documents, tile grids, collision, and the world of scenes and entities. |
| `JAG.Capsule.Runtime` | Window, device, clock, input sampling, renderer, sound playback, and save storage. |
| `JAG.Capsule.Build` | Analyzers, generators, asset hooks, atlas packing, and scene and audio import. |

`JAG.Capsule.Runtime` and `JAG.Capsule.Build` carry the [third-party notices](https://github.com/just-awesome-games/capsule-engine/blob/main/THIRD-PARTY-NOTICES.md) for the embedded default font and the atlas packer's image codecs.

Start with the [repository quickstart](https://github.com/just-awesome-games/capsule-engine#quick-start), then [game setup](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/consuming-capsule.md), [build properties](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/build-properties.md), [scene authoring](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/scenes.md), [sprite animation](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/sprite-animation.md), [texture atlases](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/atlases.md), [text](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/text.md), [headless play](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/headless-play.md), [persistence](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/persistence.md), and [testing a game](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/testing.md).

Capsule is licensed under the [MIT License](https://github.com/just-awesome-games/capsule-engine/blob/main/LICENSE).
