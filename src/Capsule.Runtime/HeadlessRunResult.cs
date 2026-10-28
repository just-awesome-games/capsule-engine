using Capsule.Rendering;

namespace Capsule.Runtime;

/// <summary>What a headless run produced.</summary>
/// <param name="Steps">
/// Fixed steps run: every step the driver or the recording supplied, unless the game requested exit
/// before the input was finished.
/// </param>
/// <param name="ExitRequested">Whether game logic ended the run instead of its input finishing.</param>
/// <param name="Metrics">The last step's render intent, counted and not drawn.</param>
/// <param name="DivergedAt">
/// The first tick a replayed recording's checksum or host act disagreed at, or null when the replay
/// matched or the run replayed nothing.
/// </param>
public readonly record struct HeadlessRunResult(long Steps, bool ExitRequested, RenderMetrics Metrics, long? DivergedAt);
