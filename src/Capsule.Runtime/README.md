# Capsule.Runtime

The host a game's shell boots. Everything that touches a device, a window or wall-clock time is here.

Contains: host bootstrapping, fixed-step scheduling, input sampling, driven play, the headless runner, rendering, scene hosting and crash logging.

Referenced by: game shell projects, and test or CI projects driving a game headlessly (game logic must not reference it; the analyzer enforces this).

API starting points: `CapsuleEngine` begins configuration, `EngineBuilder` configures and runs a scene, and `HeadlessRunResult` reports a driven headless run.

See [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md) for project wiring, [`docs/architecture.md`](../../docs/architecture.md) for the host boundary, and [`docs/headless-play.md`](../../docs/headless-play.md) for driven and headless runs. Public behavior is in the XML documentation shipped beside the assembly.
