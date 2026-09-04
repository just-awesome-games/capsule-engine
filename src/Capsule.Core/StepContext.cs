using Capsule.Input;

namespace Capsule;

/// <summary>Everything the runtime hands a simulation for one fixed step.</summary>
public readonly struct StepContext(double deltaSeconds, InputState input, long tick)
{
    private readonly double _stepSeconds = deltaSeconds;

    /// <summary>Simulated seconds this step represents; constant for a given engine configuration.</summary>
    public float DeltaSeconds { get; } = (float)deltaSeconds;

    /// <summary>Action-level input for this step; the same instance across every step of a run.</summary>
    public InputState Input { get; } = input;

    /// <summary>Index of this step; 0 on the first step ever delivered.</summary>
    public long Tick { get; } = tick;

    /// <summary>
    /// Simulated seconds at the start of this step — never wall clock. Derived from
    /// <see cref="Tick"/> and the double-precision step rather than accumulated, so it neither
    /// drifts across a long run nor carries the rounding of <see cref="DeltaSeconds"/>.
    /// </summary>
    public double TotalSeconds => Tick * _stepSeconds;
}
