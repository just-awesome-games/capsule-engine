# Capsule.Runtime

The host a game's shell boots. Everything that touches a device, a window or wall-clock time is here.

Contains: host bootstrapping, fixed-step scheduling, input sampling, driven play, the headless runner, rendering, scene hosting and crash logging.

Referenced by: game shell projects, and test or CI projects driving a game headlessly (game logic must not reference it; the analyzer enforces this).

See [`docs/architecture.md`](../../docs/architecture.md) for the module map and determinism contract, and [`docs/headless-play.md`](../../docs/headless-play.md) for driving a game and running it without a window.
