namespace Capsule.Verify;

/// <summary>The fixed-step and allocation contract for one scripted verification run.</summary>
public readonly record struct VerifyRunOptions(
    double StepSeconds,
    int WarmupSteps = 0,
    long MaxAllocatedBytesPerStep = 0,
    long MaxAllocatedBytesPerRun = 0);
