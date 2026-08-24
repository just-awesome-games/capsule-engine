using Capsule.Input;

namespace Capsule;

/// <summary>
/// Everything the runtime hands a simulation for one fixed step. Extensible by
/// addition: a new per-step channel becomes a member here rather than a new
/// parameter on <see cref="ISimulation.Step"/>.
/// </summary>
public readonly struct StepContext(double deltaSeconds, InputState input, long tick)
{
    /// <summary>Simulated seconds this step represents; constant for a given engine configuration.</summary>
    public double DeltaSeconds { get; } = deltaSeconds;

    public InputState Input { get; } = input;

    /// <summary>Index of this step; 0 on the first step ever delivered.</summary>
    public long Tick { get; } = tick;

    /// <summary>
    /// Simulated seconds at the start of this step — never wall clock. Derived rather
    /// than accumulated, so it cannot drift from <see cref="Tick"/> across a long run.
    /// </summary>
    public double TotalSeconds => Tick * DeltaSeconds;
}
