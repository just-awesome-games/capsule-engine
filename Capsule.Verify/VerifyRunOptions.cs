namespace Capsule.Verify;

/// <summary>The fixed-step and allocation contract for one scripted verification run.</summary>
/// <param name="StepSeconds">Simulated seconds each step represents; finite and positive.</param>
/// <param name="WarmupSteps">
/// Leading steps run before measurement starts, so first-touch work is not charged to the budget.
/// </param>
/// <param name="MaxAllocatedBytesPerStep">
/// Bytes the worst measured step may allocate. Null asserts nothing; <c>0</c> asserts that no
/// measured step allocates at all.
/// </param>
/// <param name="MaxAllocatedBytesPerRun">
/// Bytes every measured step together may allocate. Null asserts nothing; <c>0</c> asserts that
/// the measured run allocates nothing at all.
/// </param>
public readonly record struct VerifyRunOptions(
    double StepSeconds,
    int WarmupSteps = 0,
    long? MaxAllocatedBytesPerStep = null,
    long? MaxAllocatedBytesPerRun = null);
