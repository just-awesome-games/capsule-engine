namespace Capsule.Verify;

/// <summary>The allocation and timing summary of one scripted verification run.</summary>
/// <param name="RequestedSteps">Steps the input script held.</param>
/// <param name="CompletedSteps">Steps actually run, which is fewer where the simulation exited.</param>
/// <param name="MeasuredSteps">Completed steps past the warm-up, which are the measured ones.</param>
/// <param name="NextTick">The tick the next step would carry.</param>
/// <param name="ExitRequested">Whether the simulation asked to stop.</param>
/// <param name="AllocatedBytes">Bytes every measured step together allocated.</param>
/// <param name="PeakFrameAllocatedBytes">Bytes the worst measured step allocated.</param>
/// <param name="MeasuredDuration">Wall time every measured step together took.</param>
/// <param name="PeakFrameDuration">Wall time the worst measured step took.</param>
/// <param name="MaxAllocatedBytesPerStep">The per-step budget the run was given, if any.</param>
/// <param name="MaxAllocatedBytesPerRun">The whole-run budget the run was given, if any.</param>
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
    long? MaxAllocatedBytesPerStep,
    long? MaxAllocatedBytesPerRun)
{
    /// <summary>
    /// Whether every budget the run was given held. A budget that was never set asserts nothing
    /// and is satisfied; a run given neither is always satisfied.
    /// </summary>
    public bool AllocationBudgetSatisfied =>
        (MaxAllocatedBytesPerStep is not { } perStep || PeakFrameAllocatedBytes <= perStep) &&
        (MaxAllocatedBytesPerRun is not { } perRun || AllocatedBytes <= perRun);
}
