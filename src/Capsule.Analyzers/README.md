# Capsule.Analyzers

The compile-time enforcement of Capsule's game-logic boundary. Determinism is a property the
engine promises, and a logic assembly that reached a device, the filesystem, an ambient clock or
ambient randomness would break it silently — so the compiler refuses instead.

## Inside

`GameBoundaryAnalyzer` and its diagnostics. A project declaring either Capsule role is checked for
`CAP101`; the rest apply to the game-logic role alone.

| Id | Refuses |
| --- | --- |
| `CAP100` | a logic assembly referencing `Capsule.Runtime` |
| `CAP101` | a Capsule project referencing MonoGame directly |
| `CAP102` | external I/O |
| `CAP103` | ambient concurrency and asynchronous execution |
| `CAP104` | process or wall-clock time |
| `CAP105` | ambient randomness |

## How it ships

As an analyzer in the `JAG.Capsule.Build` package; see [`../Capsule.Build/`](../Capsule.Build/README.md).
`netstandard2.0`, because the compiler loads it into its own process, and it reaches no game
output.

## Further reading

The determinism contract the boundary serves: [`docs/architecture.md`](../../docs/architecture.md).
