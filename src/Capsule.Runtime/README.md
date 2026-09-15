# Capsule.Runtime

The host a game's shell boots: everything that touches a device, a window or wall-clock time.

Contains host bootstrapping, fixed-step scheduling, input sampling, driven play, the headless runner, rendering, sound playback, scene hosting, crash logging, and under `DevTools/` the development overlay, which a trimmed publish removes and an untrimmed one carries disabled.

Referenced by game shell projects, and by test or CI projects driving a game headlessly; game logic must not reference it, which the analyzer enforces. Public behaviour is the XML documentation shipped beside the assembly, entered through `CapsuleEngine`; [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md) holds the project wiring, [`docs/architecture.md`](../../docs/architecture.md) the host boundary, [`docs/headless-play.md`](../../docs/headless-play.md) driven runs and [`docs/debugging.md`](../../docs/debugging.md) the overlay.
