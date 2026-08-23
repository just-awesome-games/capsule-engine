using Capsule.Input;

namespace Capsule;

/// <summary>
/// Everything the runtime hands a simulation for one fixed step. Extensible by
/// addition: a new per-step channel becomes a member here rather than a new
/// parameter on <see cref="ISimulation.Step"/>.
/// </summary>
public readonly struct StepContext(double deltaSeconds, InputState input)
{
    /// <summary>Simulated seconds this step represents; constant for a given engine configuration.</summary>
    public double DeltaSeconds { get; } = deltaSeconds;

    public InputState Input { get; } = input;
}
