using Capsule.Rendering;

namespace Capsule.Runtime;

/// <summary>What a headless run produced.</summary>
/// <param name="Steps">
/// Fixed steps run: every step the driver supplied, unless the game requested exit before the
/// driver was finished.
/// </param>
/// <param name="ExitRequested">Whether game logic asked the run to end rather than the driver finishing.</param>
/// <param name="Metrics">The last step's render intent, counted rather than drawn.</param>
public readonly record struct HeadlessRunResult(long Steps, bool ExitRequested, RenderMetrics Metrics);
