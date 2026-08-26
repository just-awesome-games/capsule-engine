using Capsule.Rendering;

namespace Capsule.Verify;

/// <summary>
/// Game-provided artifact writers called after allocation measurement, so serialization and
/// graphics read-back cannot contaminate the simulation budget.
/// </summary>
public interface IVerifyArtifactSink
{
    void WriteStateDump(ISimulation simulation, in VerifyRunResult result);

    void CaptureScreenshot(FrameView view, in VerifyRunResult result);
}
