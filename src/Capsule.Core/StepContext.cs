using System.Numerics;
using Capsule.Input;

namespace Capsule;

/// <summary>Everything the runtime hands a simulation for one fixed step.</summary>
public readonly struct StepContext(double stepSeconds, InputState input, long tick, Vector2 output = default)
{
    /// <summary>The fixed step rate a run uses unless the host configures another, at 60 steps per second.</summary>
    public const int DefaultStepHertz = 60;

    // The step length at the precision the host configured. The engine's clocks derive from this, not
    // from the rounded DeltaSeconds a game reads.
    internal double StepSeconds { get; } = stepSeconds;

    /// <summary>Simulated seconds this step represents. Constant for a given engine configuration.</summary>
    public float DeltaSeconds => (float)StepSeconds;

    /// <summary>Action-level input for this step. It is the same instance across every step of a run.</summary>
    public InputState Input { get; } = input;

    /// <summary>Index of this step. The first step ever delivered is 0.</summary>
    public long Tick { get; } = tick;

    // The extent in pixels of the output this step's frame draws to. Only its aspect is read, by the
    // camera settling the region that frame shows; zero headless, where every fit is the declared span.
    internal Vector2 Output { get; } = output;

    /// <summary>
    /// Simulated seconds at the start of this step, not wall clock. It is computed from
    /// <see cref="Tick"/> and the double-precision step instead of accumulated, so it neither drifts
    /// across a long run nor carries the rounding of <see cref="DeltaSeconds"/>.
    /// </summary>
    public double TotalSeconds => Tick * StepSeconds;
}
