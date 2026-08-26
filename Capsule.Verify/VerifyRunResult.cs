namespace Capsule.Verify;

/// <summary>The allocation and timing summary of one scripted verification run.</summary>
public readonly record struct VerifyRunResult(
    int RequestedSteps,
    int CompletedSteps,
    int MeasuredSteps,
    long NextTick,
    bool ExitRequested,
    long AllocatedBytes,
    long PeakFrameAllocatedBytes,
    TimeSpan MeasuredDuration,
    TimeSpan PeakFrameDuration,
    long MaxAllocatedBytesPerStep,
    long MaxAllocatedBytesPerRun)
{
    public bool AllocationBudgetSatisfied =>
        PeakFrameAllocatedBytes <= MaxAllocatedBytesPerStep &&
        AllocatedBytes <= MaxAllocatedBytesPerRun;
}
