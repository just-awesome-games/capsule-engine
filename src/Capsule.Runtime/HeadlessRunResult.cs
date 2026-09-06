using Capsule.Rendering;

namespace Capsule.Runtime;

/// <summary>What a headless run of a tape produced.</summary>
/// <param name="Steps">
/// Fixed steps run, which is the tape's length unless the game requested exit before its last step.
/// </param>
/// <param name="ExitRequested">Whether game logic asked the run to end rather than the tape running out.</param>
/// <param name="Metrics">The last step's render intent, counted rather than drawn.</param>
public readonly record struct HeadlessRunResult(int Steps, bool ExitRequested, RenderMetrics Metrics);
