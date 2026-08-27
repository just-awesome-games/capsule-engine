using Capsule.Rendering;

namespace Capsule.Verify;

/// <summary>
/// Game-provided artifact writers called after allocation measurement, so serialization and
/// graphics read-back cannot contaminate the simulation budget.
/// </summary>
public interface IVerifyArtifactSink
{
    /// <summary>Writes whatever state the game asserts on, in whatever form it reads back in.</summary>
    /// <param name="simulation">The simulation as the run left it.</param>
    /// <param name="result">The run's own summary.</param>
    void WriteStateDump(ISimulation simulation, in VerifyRunResult result);

    /// <summary>Rasterises the final frame's render intent, however the game chooses to.</summary>
    /// <param name="view">The last frame the simulation wrote.</param>
    /// <param name="result">The run's own summary.</param>
    void CaptureScreenshot(FrameView view, in VerifyRunResult result);
}
