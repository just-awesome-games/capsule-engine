using Capsule.Rendering;

namespace Capsule.Verify;

/// <summary>Measurements for one fixed simulation frame after warm-up.</summary>
public readonly record struct VerifyFrameMetrics(
    long Tick,
    TimeSpan Duration,
    long AllocatedBytes,
    RenderMetrics Render);
